using Aether.Broker.Catalog;
using Aether.Broker.Channels;
using Aether.Broker.Health;
using Aether.Broker.Intents;
using Aether.Broker.Workspaces;
using Aether.Contracts.Json;
using Aether.Contracts.Models.Workspaces;
using ModelWindowBounds = Aether.Contracts.Models.WindowBounds;
using ModelWindowLayout = Aether.Contracts.Models.WindowLayout;
using ModelWindowPlacement = Aether.Contracts.Models.WindowPlacement;
using Aether.Broker.Launching;
using Aether.Broker.Registry;
using Aether.Broker.Sessions;
using Aether.Broker.Windows;
using Aether.Contracts;
using Aether.Contracts.Broker;
using Aether.Contracts.Capabilities;
using Aether.Contracts.Errors;
using Aether.Contracts.V1;
using Aether.Security.Audit;
using Aether.Security.Classification;
using Grpc.Core;
using DomainWindowBounds = Aether.Contracts.Models.WindowBounds;
using DomainWindowState = Aether.Contracts.Models.WindowState;
using ChannelNames = Aether.Contracts.Models.ChannelNames;
using Layouts = Aether.Contracts.Models.Layouts;

namespace Aether.Broker.Hosting;

/// <summary>
/// Broker gRPC surface. Every privileged operation checks the session capability
/// and resource grants from the trusted manifest; business denials are returned
/// as structured replies, while authentication failures map to gRPC status codes.
/// </summary>
public sealed class BrokerGrpcService : BrokerService.BrokerServiceBase
{
    private readonly BrokerOptions _options;
    private readonly AppRegistry _registry;
    private readonly ContextCatalog _contexts;
    private readonly SessionManager _sessions;
    private readonly AppLauncher _launcher;
    private readonly ChannelRouter _channels;
    private readonly WindowRegistry _windows;
    private readonly IWindowArranger _arranger;
    private readonly HealthMonitor _health;
    private readonly IAuditSink _auditSink;
    private readonly ISensitiveEntityDetector _detector;
    private readonly IntentRouter _intentRouter;
    private readonly IDisplayTopologyProvider _displayTopology;
    private readonly WorkspaceLayoutStore? _layoutStore;
    private readonly string _transport;

    public BrokerGrpcService(
        BrokerOptions options,
        AppRegistry registry,
        ContextCatalog contexts,
        SessionManager sessions,
        AppLauncher launcher,
        ChannelRouter channels,
        WindowRegistry windows,
        IWindowArranger arranger,
        HealthMonitor health,
        IAuditSink auditSink,
        IntentRouter intentRouter,
        IDisplayTopologyProvider displayTopology,
        WorkspaceLayoutStore? layoutStore,
        string transport)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _channels = channels ?? throw new ArgumentNullException(nameof(channels));
        _windows = windows ?? throw new ArgumentNullException(nameof(windows));
        _arranger = arranger ?? throw new ArgumentNullException(nameof(arranger));
        _health = health ?? throw new ArgumentNullException(nameof(health));
        _auditSink = auditSink ?? throw new ArgumentNullException(nameof(auditSink));
        _intentRouter = intentRouter ?? throw new ArgumentNullException(nameof(intentRouter));
        _displayTopology = displayTopology ?? throw new ArgumentNullException(nameof(displayTopology));
        _layoutStore = layoutStore;
        _detector = new RuleBasedSensitiveEntityDetector();
        _transport = transport;
    }

    public override Task<RegisterReply> Register(RegisterRequest request, ServerCallContext context)
    {
        var result = _sessions.Register(request, _options.TimeProvider.GetUtcNow());
        if (!result.Success || result.Session is null || result.SessionSecret is null)
        {
            throw BrokerGrpcErrors.Unauthenticated(
                result.DenialCode ?? AetherErrorCodes.AuthTokenInvalid,
                result.DenialMessage ?? "Registration was denied.");
        }

        return Task.FromResult(result.Session.ToRegisterReply(result.SessionSecret, _options));
    }

    public override Task<HeartbeatReply> Heartbeat(HeartbeatRequest request, ServerCallContext context)
    {
        var session = RequireSession(context);
        _sessions.RecordHeartbeat(session, _options.TimeProvider.GetUtcNow());
        var report = _health.Evaluate().FirstOrDefault(item => string.Equals(item.InstanceId, session.InstanceId, StringComparison.Ordinal));
        return Task.FromResult(new HeartbeatReply
        {
            ServerTimeUnixMs = _options.TimeProvider.GetUtcNow().ToUnixTimeMilliseconds(),
            SessionValid = true,
            HealthState = (report?.State ?? AppHealthState.Healthy).ToString().ToUpperInvariant(),
        });
    }

    public override Task<PublishContextReply> PublishContext(PublishContextRequest request, ServerCallContext context)
    {
        var session = RequireSession(context);
        RequireCapability(session, CapabilityNames.ContextPublish);
        var traceId = ResolveTraceId(request.TraceId, context);
        var reply = new PublishContextReply();

        if (!_contexts.TryGet(request.ContextRef, out var entry))
        {
            reply.DenialCode = AetherErrorCodes.BrokerContextNotFound;
            reply.DenialMessage = "The referenced context is not present in the trusted catalog.";
            AuditPublishDenied(traceId, session, reply.DenialCode);
            return Task.FromResult(reply);
        }

        var requested = request.SourceClassification.FromProto();
        if (requested == DataClassification.Unknown)
        {
            reply.DenialCode = AetherErrorCodes.PolicyClassificationUnknown;
            reply.DenialMessage = "A context with unknown classification cannot be published (fail-closed).";
            AuditPublishDenied(traceId, session, reply.DenialCode);
            return Task.FromResult(reply);
        }

        if (requested.SensitivityRank() < entry.Classification.SensitivityRank())
        {
            reply.DenialCode = AetherErrorCodes.AuthClassificationDenied;
            reply.DenialMessage = "The declared source classification may not downgrade the trusted context classification.";
            AuditPublishDenied(traceId, session, reply.DenialCode);
            return Task.FromResult(reply);
        }

        if (!session.AllowedContextTypes.Contains(entry.ContextType))
        {
            reply.DenialCode = AetherErrorCodes.AuthContextTypeDenied;
            reply.DenialMessage = "The session is not allowed to publish this context type.";
            AuditPublishDenied(traceId, session, reply.DenialCode);
            return Task.FromResult(reply);
        }

        if (!session.AllowedClassifications.Contains(requested) || !session.AllowedClassifications.Contains(entry.Classification))
        {
            reply.DenialCode = AetherErrorCodes.AuthClassificationDenied;
            reply.DenialMessage = "The session is not cleared for the classification of this context.";
            AuditPublishDenied(traceId, session, reply.DenialCode);
            return Task.FromResult(reply);
        }

        if (request.DeadlineUnixMs > 0 && _options.TimeProvider.GetUtcNow().ToUnixTimeMilliseconds() > request.DeadlineUnixMs)
        {
            reply.DenialCode = AetherErrorCodes.ExecutionDeadlineExceeded;
            reply.DenialMessage = "The publish request deadline has expired.";
            AuditPublishDenied(traceId, session, reply.DenialCode);
            return Task.FromResult(reply);
        }

        var spanId = TraceIdGenerator.NewSpanId();
        var now = _options.TimeProvider.GetUtcNow();
        var envelope = new ContextEnvelope
        {
            MessageId = Guid.NewGuid().ToString("n"),
            TraceId = traceId,
            SpanId = spanId,
            SchemaVersion = AetherVersions.ContractSchemaVersion,
            ContextType = entry.ContextType,
            Classification = requested.ToProto(),
            CreatedAtUnixMs = now.ToUnixTimeMilliseconds(),
            DeadlineUnixMs = request.DeadlineUnixMs,
            PayloadJson = entry.PayloadJson,
            Channel = string.IsNullOrWhiteSpace(request.Channel) ? ChannelNames.Default : request.Channel,
            Provenance = new Provenance
            {
                SourceAppId = session.AppId,
                SourceInstanceId = session.InstanceId,
                TraceId = traceId,
                SpanId = spanId,
                CreatedAtUnixMs = now.ToUnixTimeMilliseconds(),
            },
        };

        var result = _channels.Publish(session, envelope, request.ClientMessageId);
        if (!result.Accepted)
        {
            reply.DenialCode = result.DenialCode;
            reply.DenialMessage = result.DenialMessage;
            AuditPublishDenied(traceId, session, result.DenialCode);
            return Task.FromResult(reply);
        }

        var detection = _detector.Detect(entry.PayloadJson);
        _auditSink.Append(new AuditEventDraft
        {
            TraceId = traceId,
            EventType = AuditEventTypes.ContextPublish,
            Outcome = AuditOutcomes.Succeeded,
            Classification = requested.ToWireName(),
            RedactionCount = detection.Failed ? 0 : detection.TotalCount,
            PayloadBytes = entry.PayloadBytes,
            BrokeredAppId = session.AppId,
        });

        reply.Accepted = true;
        reply.MessageId = result.MessageId;
        reply.DeliveredCount = result.DeliveredCount;
        reply.DroppedCount = result.DroppedCount;
        reply.Overflowed = result.Overflowed;
        return Task.FromResult(reply);
    }

    public override async Task Subscribe(SubscribeRequest request, IServerStreamWriter<ContextEnvelope> responseStream, ServerCallContext context)
    {
        var session = RequireSession(context);
        RequireCapability(session, CapabilityNames.ContextSubscribe);
        var channel = string.IsNullOrWhiteSpace(request.Channel) ? ChannelNames.Default : request.Channel;
        await using var subscription = _channels.Subscribe(session, channel);

        try
        {
            while (await subscription.Reader.WaitToReadAsync(context.CancellationToken).ConfigureAwait(false))
            {
                while (subscription.Reader.TryRead(out var envelope))
                {
                    await responseStream.WriteAsync(envelope).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            // Client disconnected; subscription is disposed by the using scope.
        }
    }

    public override Task<ResolveContextReply> ResolveContext(ResolveContextRequest request, ServerCallContext context)
    {
        var session = RequireSession(context);
        RequireCapability(session, CapabilityNames.ContextRead);
        var traceId = ResolveTraceId(request.TraceId, context);
        var reply = new ResolveContextReply();

        if (!_contexts.TryGet(request.ContextRef, out var entry))
        {
            reply.DenialCode = AetherErrorCodes.BrokerContextNotFound;
            reply.DenialMessage = "The referenced context is not present in the trusted catalog.";
            return Task.FromResult(reply);
        }

        if (!session.AllowedClassifications.Contains(entry.Classification))
        {
            reply.DenialCode = AetherErrorCodes.AuthClassificationDenied;
            reply.DenialMessage = "The session is not cleared for the classification of this context.";
            return Task.FromResult(reply);
        }

        var now = _options.TimeProvider.GetUtcNow();
        reply.Allowed = true;
        reply.ContextRef = entry.ContextRef;
        reply.ContextType = entry.ContextType;
        reply.Classification = entry.Classification.ToProto();
        reply.PayloadJson = entry.PayloadJson;
        reply.Provenance = new Provenance
        {
            SourceAppId = "aether.broker",
            SourceInstanceId = "broker",
            TraceId = traceId,
            SpanId = TraceIdGenerator.NewSpanId(),
            CreatedAtUnixMs = now.ToUnixTimeMilliseconds(),
        };
        return Task.FromResult(reply);
    }

    public override Task<ReportWindowReply> ReportWindow(ReportWindowRequest request, ServerCallContext context)
    {
        var session = RequireSession(context);
        RequireCapability(session, CapabilityNames.WindowReport);

        var state = new DomainWindowState(
            session.AppId,
            session.InstanceId,
            request.WindowId ?? "",
            request.Title ?? "",
            request.Bounds is null
                ? new DomainWindowBounds(0, 0, 0, 0)
                : new DomainWindowBounds(request.Bounds.X, request.Bounds.Y, request.Bounds.Width, request.Bounds.Height),
            string.IsNullOrWhiteSpace(request.MonitorId) ? _options.MonitorId : request.MonitorId,
            request.Visible,
            request.NativeHandle);

        var accepted = _windows.Report(session, state, out var denialCode, out var denialMessage);
        return Task.FromResult(new ReportWindowReply
        {
            Accepted = accepted,
            DenialCode = denialCode ?? "",
        });
    }

    public override Task<LaunchAppReply> LaunchApp(LaunchAppRequest request, ServerCallContext context)
    {
        var session = RequireSession(context);
        RequireCapability(session, CapabilityNames.BrokerLaunch);
        var traceId = ResolveTraceId(request.TraceId, context);
        return LaunchCoreAsync(request.AppId ?? "", traceId, context.CancellationToken);
    }

    private async Task<LaunchAppReply> LaunchCoreAsync(string appId, string traceId, CancellationToken cancellationToken)
    {
        var reply = new LaunchAppReply { AppId = appId };
        if (!_registry.TryGet(appId, out var app))
        {
            reply.DenialCode = AetherErrorCodes.BrokerAppUnknown;
            reply.DenialMessage = "The requested application is not registered in the allowlist.";
            return reply;
        }

        var outcome = await _launcher.LaunchAsync(appId, traceId, cancellationToken).ConfigureAwait(false);
        if (!outcome.Success || outcome.App is null)
        {
            reply.DenialCode = outcome.DenialCode ?? AetherErrorCodes.BrokerLaunchFailed;
            reply.DenialMessage = outcome.DenialMessage ?? "The application could not be launched.";
            return reply;
        }

        var registeredSession = await _sessions
            .WaitForRegistrationAsync(outcome.App.InstanceId, app.LaunchTimeout, cancellationToken)
            .ConfigureAwait(false);

        if (registeredSession is null)
        {
            await _launcher.StopAsync(appId, outcome.App.InstanceId, traceId, CancellationToken.None).ConfigureAwait(false);
            reply.DenialCode = AetherErrorCodes.BrokerLaunchTimeout;
            reply.DenialMessage = "The application started but did not register within the configured timeout.";
            return reply;
        }

        if (_options.RequireAppReadiness &&
            !await WaitForAppReadinessAsync(outcome.App.InstanceId, appId, cancellationToken).ConfigureAwait(false))
        {
            await _launcher.StopAsync(appId, outcome.App.InstanceId, traceId, CancellationToken.None).ConfigureAwait(false);
            reply.DenialCode = AetherErrorCodes.BrokerLaunchTimeout;
            reply.DenialMessage = "The application registered but did not become ready (no channel subscription or window) within the configured timeout.";
            return reply;
        }

        reply.Accepted = true;
        reply.AppId = appId;
        reply.InstanceId = outcome.App.InstanceId;
        reply.ProcessId = outcome.App.ProcessId;
        reply.Registered = true;
        return reply;
    }

    private async Task<bool> WaitForAppReadinessAsync(string instanceId, string appId, CancellationToken cancellationToken)
    {
        var registryEntry = _registry.GetRequired(appId);
        var requiresSubscription = registryEntry.Capabilities.Contains(CapabilityNames.ContextSubscribe);
        var deadline = _options.TimeProvider.GetUtcNow() + TimeSpan.FromSeconds(_options.AppReadinessTimeoutSeconds);
        while (_options.TimeProvider.GetUtcNow() < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ready = requiresSubscription
                ? _channels.SubscriberCountForInstance(instanceId) > 0
                : _windows.HasWindowForInstance(instanceId);
            if (ready)
            {
                return true;
            }

            if (!_sessions.SnapshotSessions().Any(session => string.Equals(session.InstanceId, instanceId, StringComparison.Ordinal)))
            {
                return false;
            }

            await Task.Delay(25, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    public override async Task<StopAppReply> StopApp(StopAppRequest request, ServerCallContext context)
    {
        var session = RequireSession(context);
        RequireCapability(session, CapabilityNames.BrokerLaunch);
        var traceId = ResolveTraceId(request.TraceId, context);
        var outcome = await _launcher
            .StopAsync(request.AppId ?? "", string.IsNullOrWhiteSpace(request.InstanceId) ? null : request.InstanceId, traceId, context.CancellationToken)
            .ConfigureAwait(false);

        return new StopAppReply
        {
            Stopped = outcome.Stopped,
            DenialCode = outcome.DenialCode ?? "",
            DenialMessage = outcome.DenialMessage ?? "",
        };
    }

    public override async Task<RestartAppReply> RestartApp(RestartAppRequest request, ServerCallContext context)
    {
        var session = RequireSession(context);
        RequireCapability(session, CapabilityNames.BrokerLaunch);
        var traceId = ResolveTraceId(request.TraceId, context);
        var appId = request.AppId ?? "";
        var outcome = await _launcher.RestartAsync(appId, traceId, context.CancellationToken).ConfigureAwait(false);
        if (outcome.Success && outcome.App is not null)
        {
            var app = _registry.GetRequired(appId);
            var registered = await _sessions
                .WaitForRegistrationAsync(outcome.App.InstanceId, app.LaunchTimeout, context.CancellationToken)
                .ConfigureAwait(false);
            if (registered is null)
            {
                await _launcher.StopAsync(appId, outcome.App.InstanceId, traceId, CancellationToken.None).ConfigureAwait(false);
                return new RestartAppReply
                {
                    Accepted = false,
                    FailureState = AppHealthState.Unknown.ToString().ToUpperInvariant(),
                    DenialCode = AetherErrorCodes.BrokerLaunchTimeout,
                    DenialMessage = "The restarted application did not register within the configured timeout.",
                };
            }
        }

        var report = _health.Evaluate()
            .FirstOrDefault(item => string.Equals(item.AppId, appId, StringComparison.Ordinal));
        return new RestartAppReply
        {
            Accepted = outcome.Success,
            InstanceId = outcome.App?.InstanceId ?? "",
            FailureState = report?.State.ToString().ToUpperInvariant() ?? AppHealthState.Unknown.ToString().ToUpperInvariant(),
            DenialCode = outcome.DenialCode ?? "",
            DenialMessage = outcome.DenialMessage ?? "",
        };
    }

    public override async Task<ArrangeWindowsReply> ArrangeWindows(ArrangeWindowsRequest request, ServerCallContext context)
    {
        var session = RequireSession(context);
        RequireCapability(session, CapabilityNames.BrokerArrange);
        var traceId = ResolveTraceId(request.TraceId, context);
        var layoutName = string.IsNullOrWhiteSpace(request.Layout) ? Layouts.SideBySide : request.Layout;

        if (!_options.SupportedLayouts.Contains(layoutName))
        {
            return new ArrangeWindowsReply
            {
                Applied = false,
                DenialCode = AetherErrorCodes.PlanLayoutUnsupported,
                DenialMessage = "The requested layout is not supported by this broker.",
            };
        }

        var windows = _windows.ListVisible();
        if (windows.Count == 0)
        {
            return new ArrangeWindowsReply
            {
                Applied = false,
                DenialCode = AetherErrorCodes.BrokerArrangeNoWindows,
                DenialMessage = "No registered top-level windows have been reported yet.",
            };
        }

        var layout = LayoutPlanner.Plan(windows, _registry.Ordered, layoutName, _options);
        var result = await _arranger.ApplyAsync(layout, windows, context.CancellationToken).ConfigureAwait(false);

        _auditSink.Append(new AuditEventDraft
        {
            TraceId = traceId,
            EventType = AuditEventTypes.WindowArrange,
            Outcome = result.Applied ? AuditOutcomes.Succeeded : AuditOutcomes.Failed,
            BrokeredAppId = "broker",
        });

        var reply = new ArrangeWindowsReply
        {
            Applied = result.Applied,
            Layout = new WindowLayout
            {
                Layout = layout.Layout,
                MonitorId = layout.MonitorId,
            },
        };
        reply.Layout.Placements.AddRange(layout.Placements.Select(placement => new WindowPlacement
        {
            AppId = placement.AppId,
            InstanceId = placement.InstanceId,
            WindowId = placement.WindowId,
            Title = placement.Title,
            MonitorId = placement.MonitorId,
            Visible = placement.Visible,
            Bounds = new WindowBounds
            {
                X = placement.Bounds.X,
                Y = placement.Bounds.Y,
                Width = placement.Bounds.Width,
                Height = placement.Bounds.Height,
            },
        }));

        if (!result.Applied)
        {
            reply.DenialCode = AetherErrorCodes.BrokerArrangeNoWindows;
            reply.DenialMessage = result.Message ?? "Window arrangement was not applied.";
        }

        return reply;
    }

    public override Task<SnapshotReply> GetSnapshot(SnapshotRequest request, ServerCallContext context)
    {
        var session = RequireSession(context);

        var reply = new SnapshotReply
        {
            BrokerVersion = _options.BrokerVersion,
            ServerTimeUnixMs = _options.TimeProvider.GetUtcNow().ToUnixTimeMilliseconds(),
            MaxActionsPerPlan = _options.MaxActionsPerPlan,
            PolicyVersion = _options.PolicyVersion,
        };
        reply.SupportedLayouts.AddRange(_options.SupportedLayouts);

        foreach (var app in _registry.Ordered)
        {
            var summary = new AppSummary
            {
                AppId = app.AppId,
                DisplayName = app.DisplayName,
                Launchable = app.Launchable,
            };
            summary.Capabilities.AddRange(app.Capabilities);
            summary.AllowedChannels.AddRange(app.AllowedChannels);
            summary.AllowedContextTypes.AddRange(app.AllowedContextTypes);
            summary.AllowedClassifications.AddRange(app.AllowedClassifications.Select(classification => classification.ToProto()));
            summary.Intents.AddRange(app.Intents);
            reply.Apps.Add(summary);
        }

        foreach (var contextRef in _contexts.ContextRefs.OrderBy(value => value, StringComparer.Ordinal))
        {
            _contexts.TryGet(contextRef, out var entry);
            reply.Contexts.Add(entry.ToSummary());
        }

        _ = session;
        return Task.FromResult(reply);
    }

    public override Task<ResolveIntentReply> ResolveIntent(ResolveIntentRequest request, ServerCallContext context)
    {
        var session = RequireSession(context);
        RequireCapability(session, CapabilityNames.IntentResolve);
        var traceId = ResolveTraceId(request.TraceId, context);

        var resolution = _intentRouter.Resolve(request.Intent, request.ContextRef, request.PreferredAppId);
        _auditSink.Append(new AuditEventDraft
        {
            TraceId = traceId,
            EventType = AuditEventTypes.IntentResolve,
            Outcome = resolution.Resolved ? AuditOutcomes.Succeeded : AuditOutcomes.Denied,
            Code = resolution.DenialCode,
            Classification = resolution.ContextType.Length == 0 ? null : null,
            BrokeredAppId = resolution.AppId ?? request.PreferredAppId,
        });

        var now = _options.TimeProvider.GetUtcNow();
        return Task.FromResult(new ResolveIntentReply
        {
            Resolved = resolution.Resolved,
            Intent = resolution.Intent,
            ContextRef = resolution.ContextRef,
            ContextType = resolution.ContextType,
            AppId = resolution.AppId ?? "",
            DisplayName = resolution.DisplayName ?? "",
            DenialCode = resolution.DenialCode ?? "",
            DenialMessage = resolution.DenialMessage ?? "",
            Provenance = new Provenance
            {
                SourceAppId = "aether.broker",
                SourceInstanceId = "broker",
                TraceId = traceId,
                SpanId = TraceIdGenerator.NewSpanId(),
                CreatedAtUnixMs = now.ToUnixTimeMilliseconds(),
            },
        });
    }

    public override Task<SaveWorkspaceLayoutReply> SaveWorkspaceLayout(SaveWorkspaceLayoutRequest request, ServerCallContext context)
    {
        var session = RequireSession(context);
        RequireCapability(session, CapabilityNames.WorkspaceLayoutSave);
        var traceId = ResolveTraceId(request.TraceId, context);
        if (_layoutStore is null)
        {
            return Task.FromResult(new SaveWorkspaceLayoutReply
            {
                Accepted = false,
                DenialCode = AetherErrorCodes.WorkspaceLayoutUnavailable,
                DenialMessage = "Durable workspace layouts are disabled on this broker (no layout store configured).",
            });
        }

        var layoutId = string.IsNullOrWhiteSpace(request.LayoutId) ? "default" : request.LayoutId.Trim();
        try
        {
            var windows = _windows.ListVisible();
            if (windows.Count == 0)
            {
                return Task.FromResult(new SaveWorkspaceLayoutReply
                {
                    Accepted = false,
                    DenialCode = AetherErrorCodes.BrokerArrangeNoWindows,
                    DenialMessage = "No registered top-level windows have been reported yet.",
                });
            }

            var monitors = _displayTopology.GetMonitors();
            if (monitors.Count == 0)
            {
                return Task.FromResult(new SaveWorkspaceLayoutReply
                {
                    Accepted = false,
                    DenialCode = AetherErrorCodes.WorkspaceLayoutUnavailable,
                    DenialMessage = "No display topology is available; refusing to persist an empty layout.",
                });
            }

            var layout = WorkspaceLayoutPlanner.Capture(
                layoutId,
                monitors,
                windows,
                _options.TimeProvider.GetUtcNow(),
                string.IsNullOrWhiteSpace(request.Description) ? null : request.Description);
            _layoutStore.Save(layout);

            _auditSink.Append(new AuditEventDraft
            {
                TraceId = traceId,
                EventType = AuditEventTypes.WorkspaceLayoutSave,
                Outcome = AuditOutcomes.Succeeded,
                BrokeredAppId = "broker",
            });

            return Task.FromResult(new SaveWorkspaceLayoutReply
            {
                Accepted = true,
                LayoutId = layout.LayoutId,
                LayoutJson = AetherJson.Serialize(layout),
                Provenance = CreateBrokerProvenance(traceId),
            });
        }
        catch (AetherContractException exception)
        {
            _auditSink.Append(new AuditEventDraft
            {
                TraceId = traceId,
                EventType = AuditEventTypes.WorkspaceLayoutSave,
                Outcome = AuditOutcomes.Denied,
                Code = exception.Code,
                BrokeredAppId = "broker",
            });
            return Task.FromResult(new SaveWorkspaceLayoutReply
            {
                Accepted = false,
                DenialCode = exception.Code,
                DenialMessage = exception.Message,
            });
        }
        catch (IOException exception)
        {
            _auditSink.Append(new AuditEventDraft
            {
                TraceId = traceId,
                EventType = AuditEventTypes.WorkspaceLayoutSave,
                Outcome = AuditOutcomes.Failed,
                Code = AetherErrorCodes.WorkspaceLayoutUnavailable,
                BrokeredAppId = "broker",
            });
            return Task.FromResult(new SaveWorkspaceLayoutReply
            {
                Accepted = false,
                DenialCode = AetherErrorCodes.WorkspaceLayoutUnavailable,
                DenialMessage = exception.Message,
            });
        }
    }

    public override async Task<RestoreWorkspaceLayoutReply> RestoreWorkspaceLayout(RestoreWorkspaceLayoutRequest request, ServerCallContext context)
    {
        var session = RequireSession(context);
        RequireCapability(session, CapabilityNames.WorkspaceLayoutRestore);
        var traceId = ResolveTraceId(request.TraceId, context);
        if (_layoutStore is null)
        {
            return new RestoreWorkspaceLayoutReply
            {
                Applied = false,
                DenialCode = AetherErrorCodes.WorkspaceLayoutUnavailable,
                DenialMessage = "Durable workspace layouts are disabled on this broker (no layout store configured).",
            };
        }

        var load = _layoutStore.Load(request.LayoutId);
        if (!load.Found || load.Layout is null)
        {
            return new RestoreWorkspaceLayoutReply
            {
                Applied = false,
                LayoutId = request.LayoutId,
                DenialCode = load.Code ?? AetherErrorCodes.WorkspaceLayoutNotFound,
                DenialMessage = load.Message ?? "The workspace layout could not be loaded.",
            };
        }

        var monitors = _displayTopology.GetMonitors();
        var plan = WorkspaceLayoutPlanner.PlanRestore(load.Layout, monitors);
        var windows = _windows.ListVisible();
        var windowByApp = windows
            .GroupBy(window => window.AppId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(window => window.InstanceId, StringComparer.Ordinal).First(), StringComparer.Ordinal);

        var placements = new List<ModelWindowPlacement>();
        foreach (var planned in plan.Placements)
        {
            if (!windowByApp.TryGetValue(planned.AppId, out var window))
            {
                continue;
            }

            placements.Add(new ModelWindowPlacement(
                window.AppId,
                window.InstanceId,
                window.WindowId,
                window.Title,
                new ModelWindowBounds(planned.Bounds.X, planned.Bounds.Y, planned.Bounds.Width, planned.Bounds.Height),
                planned.MonitorDeviceName,
                planned.Visible));
        }

        var reply = new RestoreWorkspaceLayoutReply
        {
            LayoutId = load.Layout.LayoutId,
            ClampedCount = plan.ClampedCount,
            PrimaryFallbackCount = plan.PrimaryFallbackCount,
            MissingMonitorCount = plan.MissingMonitorCount,
            DpiScaledCount = plan.DpiScaledCount,
            Provenance = CreateBrokerProvenance(traceId),
        };
        reply.Notes.AddRange(plan.Notes);
        reply.Placements.AddRange(placements.Select(placement => new WorkspaceLayoutPlacement
        {
            AppId = placement.AppId,
            MonitorDeviceName = placement.MonitorId,
            X = placement.Bounds.X,
            Y = placement.Bounds.Y,
            Width = placement.Bounds.Width,
            Height = placement.Bounds.Height,
            Visible = placement.Visible,
        }));

        if (placements.Count == 0)
        {
            reply.Applied = false;
            reply.DenialCode = AetherErrorCodes.BrokerArrangeNoWindows;
            reply.DenialMessage = "No currently registered windows match the saved workspace layout.";
            return reply;
        }

        var layoutToApply = new ModelWindowLayout("workspace-restore", placements, placements[0].MonitorId);
        var applied = await _arranger.ApplyAsync(layoutToApply, windows, context.CancellationToken).ConfigureAwait(false);
        reply.Applied = applied.Applied;
        if (!applied.Applied)
        {
            reply.DenialCode = AetherErrorCodes.BrokerArrangeNoWindows;
            reply.DenialMessage = applied.Message ?? "The workspace layout was not applied.";
        }

        _auditSink.Append(new AuditEventDraft
        {
            TraceId = traceId,
            EventType = AuditEventTypes.WorkspaceLayoutRestore,
            Outcome = applied.Applied ? AuditOutcomes.Succeeded : AuditOutcomes.Failed,
            Code = reply.DenialCode,
            BrokeredAppId = "broker",
        });

        return reply;
    }

    private Provenance CreateBrokerProvenance(string traceId) => new()
    {
        SourceAppId = "aether.broker",
        SourceInstanceId = "broker",
        TraceId = traceId,
        SpanId = TraceIdGenerator.NewSpanId(),
        CreatedAtUnixMs = _options.TimeProvider.GetUtcNow().ToUnixTimeMilliseconds(),
    };

    public override Task<HealthProbeReply> HealthProbe(HealthProbeRequest request, ServerCallContext context)
    {
        var authenticated = context.UserState.TryGetValue(BrokerMetadataKeys.UserStateSession, out var session);
        var reply = new HealthProbeReply
        {
            BrokerVersion = _options.BrokerVersion,
            PolicyVersion = _options.PolicyVersion,
            Transport = _transport,
            ActiveSessions = _sessions.ActiveSessionCount,
            RegisteredWindows = _windows.Count,
        };

        if (authenticated && session is not null)
        {
            reply.AppHealth.AddRange(_health.Evaluate().Select(report => $"{report.AppId}={report.State.ToString().ToUpperInvariant()}"));
        }

        return Task.FromResult(reply);
    }

    private void AuditPublishDenied(string traceId, BrokerSession session, string? code) =>
        _auditSink.Append(new AuditEventDraft
        {
            TraceId = traceId,
            EventType = AuditEventTypes.ContextPublish,
            Outcome = AuditOutcomes.Denied,
            Code = code,
            BrokeredAppId = session.AppId,
        });

    private static BrokerSession RequireSession(ServerCallContext context)
    {
        if (context.UserState.TryGetValue(BrokerMetadataKeys.UserStateSession, out var value) && value is BrokerSession session)
        {
            return session;
        }

        throw BrokerGrpcErrors.Unauthenticated(AetherErrorCodes.AuthSessionInvalid, "A valid broker session is required for this operation.");
    }

    private static void RequireCapability(BrokerSession session, string capability)
    {
        if (!session.HasCapability(capability))
        {
            throw BrokerGrpcErrors.PermissionDenied(
                AetherErrorCodes.AuthCapabilityMissing,
                "The current session is not allowed to perform this operation.");
        }
    }

    private string ResolveTraceId(string? requestedTraceId, ServerCallContext context)
    {
        if (TraceContext.IsValidTraceId(requestedTraceId))
        {
            return requestedTraceId!;
        }

        var metadataTraceId = BrokerAuthInterceptor.ReadHeader(context, BrokerMetadataKeys.TraceId);
        return TraceContext.IsValidTraceId(metadataTraceId) ? metadataTraceId! : TraceIdGenerator.NewTraceId();
    }
}
