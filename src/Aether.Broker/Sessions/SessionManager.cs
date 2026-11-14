using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Aether.Broker.Launching;
using Aether.Broker.Registry;
using Aether.Contracts;
using Aether.Contracts.Broker;
using Aether.Contracts.Capabilities;
using Aether.Contracts.Errors;
using Aether.Contracts.V1;
using Aether.Security.Audit;

namespace Aether.Broker.Sessions;

/// <summary>
/// Issues and validates broker sessions. App sessions are bound to the process
/// the broker launched (pid, start time, canonical executable path) and to the
/// short-lived single-use registration token delivered through the child
/// environment. The operator token is a current-user-only file credential used
/// by the shell; it is reusable within a bounded budget and documented as a
/// residual risk.
/// </summary>
public sealed class SessionManager
{
    private readonly object _gate = new();
    private readonly BrokerOptions _options;
    private readonly AppRegistry _registry;
    private readonly IProcessIdentityResolver _identityResolver;
    private readonly IAuditSink _auditSink;
    private readonly Dictionary<string, BrokerSession> _sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BrokerSession> _byInstance = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PendingRegistration> _pendingByTokenHash = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PendingRegistration> _pendingByInstance = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TaskCompletionSource<BrokerSession?>> _registrationWaiters = new(StringComparer.Ordinal);
    private readonly string _operatorToken;
    private int _operatorTokenUses;

    public SessionManager(
        BrokerOptions options,
        AppRegistry registry,
        IProcessIdentityResolver identityResolver,
        IAuditSink auditSink)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _identityResolver = identityResolver ?? throw new ArgumentNullException(nameof(identityResolver));
        _auditSink = auditSink ?? throw new ArgumentNullException(nameof(auditSink));
        _operatorToken = string.IsNullOrEmpty(options.OperatorToken)
            ? TokenGenerator.NewToken()
            : options.OperatorToken;
    }

    public string OperatorToken => _operatorToken;

    public int ActiveSessionCount
    {
        get
        {
            lock (_gate)
            {
                PruneExpiredLocked(_options.TimeProvider.GetUtcNow());
                return _sessions.Count;
            }
        }
    }

    public BrokerSession? GetActiveInstance(string appId)
    {
        lock (_gate)
        {
            PruneExpiredLocked(_options.TimeProvider.GetUtcNow());
            return _byInstance.Values
                .Where(session => string.Equals(session.AppId, appId, StringComparison.Ordinal))
                .OrderByDescending(session => session.CreatedAtUtc)
                .FirstOrDefault();
        }
    }

    public IReadOnlyList<BrokerSession> SnapshotSessions()
    {
        lock (_gate)
        {
            PruneExpiredLocked(_options.TimeProvider.GetUtcNow());
            return _sessions.Values.ToArray();
        }
    }

    /// <summary>Registers a pending launch token for a process the broker just started.</summary>
    public string AddPendingRegistration(
        RegisteredApp app,
        string instanceId,
        string token,
        ProcessIdentity expectedIdentity,
        DateTimeOffset now)
    {
        var tokenHash = TokenGenerator.Hash(token);
        var pending = new PendingRegistration(
            app,
            instanceId,
            tokenHash,
            SystemProcessIdentityResolver.CanonicalizeExecutable(expectedIdentity.ExecutablePath),
            expectedIdentity.ProcessId,
            expectedIdentity.StartTimeUtc,
            now.AddSeconds(_options.PendingRegistrationTtlSeconds));

        lock (_gate)
        {
            PrunePendingLocked(now);
            _pendingByTokenHash[tokenHash] = pending;
            _pendingByInstance[instanceId] = pending;
            _registrationWaiters.TryAdd(instanceId, new TaskCompletionSource<BrokerSession?>(TaskCreationOptions.RunContinuationsAsynchronously));
        }

        return token;
    }

    /// <summary>Waits until the launched instance registers and returns its session.</summary>
    public async Task<BrokerSession?> WaitForRegistrationAsync(string instanceId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        TaskCompletionSource<BrokerSession?> waiter;
        lock (_gate)
        {
            if (_byInstance.TryGetValue(instanceId, out var alreadyRegistered))
            {
                return alreadyRegistered;
            }

            if (!_registrationWaiters.TryGetValue(instanceId, out waiter!))
            {
                waiter = new TaskCompletionSource<BrokerSession?>(TaskCreationOptions.RunContinuationsAsynchronously);
                _registrationWaiters[instanceId] = waiter;
            }
        }

        try
        {
            return await waiter.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return null;
        }
        finally
        {
            lock (_gate)
            {
                if (_registrationWaiters.TryGetValue(instanceId, out var existing) && ReferenceEquals(existing, waiter))
                {
                    _registrationWaiters.Remove(instanceId);
                }
            }
        }
    }

    public SessionRegistrationResult Register(RegisterRequest request, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!IsSupportedProtocol(request.ProtocolVersion))
        {
            AuditRegistrationDenied(request, AetherErrorCodes.AuthProtocolUnsupported);
            return SessionRegistrationResult.Deny(
                AetherErrorCodes.AuthProtocolUnsupported,
                "The app speaks an unsupported broker protocol version.");
        }

        if (string.IsNullOrWhiteSpace(request.AppId) || string.IsNullOrWhiteSpace(request.InstanceId))
        {
            AuditRegistrationDenied(request, AetherErrorCodes.AuthTokenInvalid);
            return SessionRegistrationResult.Deny(AetherErrorCodes.AuthTokenInvalid, "Registration request is incomplete.");
        }

        if (_options.OperatorAppId is not null && string.Equals(request.AppId, _options.OperatorAppId, StringComparison.Ordinal))
        {
            return RegisterOperator(request, now);
        }

        PendingRegistration? pending;
        var tokenHash = TokenGenerator.Hash(request.RegistrationToken ?? "");
        lock (_gate)
        {
            PruneExpiredLocked(now);
            PrunePendingLocked(now);
            _pendingByTokenHash.TryGetValue(tokenHash, out pending);
        }

        if (pending is null)
        {
            AuditRegistrationDenied(request, AetherErrorCodes.AuthTokenInvalid);
            return SessionRegistrationResult.Deny(
                AetherErrorCodes.AuthTokenInvalid,
                "The registration token is unknown, expired or already used.");
        }

        if (!string.Equals(pending.App.AppId, request.AppId, StringComparison.Ordinal) ||
            !string.Equals(pending.InstanceId, request.InstanceId, StringComparison.Ordinal))
        {
            AuditRegistrationDenied(request, AetherErrorCodes.AuthIdentityMismatch);
            return SessionRegistrationResult.Deny(
                AetherErrorCodes.AuthIdentityMismatch,
                "The registration request does not match the launched application instance.");
        }

        var observed = _identityResolver.Resolve(request.ProcessId);
        if (observed is null)
        {
            AuditRegistrationDenied(request, AetherErrorCodes.AuthIdentityMismatch);
            return SessionRegistrationResult.Deny(
                AetherErrorCodes.AuthIdentityMismatch,
                "The calling process could not be verified.");
        }

        if (!ProcessPathComparer.PathsEqual(observed.ExecutablePath, pending.ExpectedExecutablePath) ||
            observed.ProcessId != pending.ExpectedProcessId ||
            Math.Abs((observed.StartTimeUtc - pending.ExpectedStartTimeUtc).TotalMilliseconds) > 2000)
        {
            AuditRegistrationDenied(request, AetherErrorCodes.AuthIdentityMismatch);
            return SessionRegistrationResult.Deny(
                AetherErrorCodes.AuthIdentityMismatch,
                "The calling process identity does not match the launched application instance.");
        }

        BrokerSession session;
        lock (_gate)
        {
            PruneExpiredLocked(now);
            if (!_pendingByTokenHash.Remove(tokenHash, out var current) || !ReferenceEquals(current, pending))
            {
                AuditRegistrationDenied(request, AetherErrorCodes.AuthTokenReplayed);
                return SessionRegistrationResult.Deny(
                    AetherErrorCodes.AuthTokenReplayed,
                    "The registration token has already been used.");
            }

            _pendingByInstance.Remove(pending.InstanceId);
            session = CreateSessionLocked(pending.App, pending.InstanceId, observed, now, out var secret);
            CompleteWaiterLocked(pending.InstanceId, session);
            _auditSink.Append(new AuditEventDraft
            {
                TraceId = TraceIdGenerator.NewTraceId(),
                EventType = AuditEventTypes.BrokerSession,
                Outcome = AuditOutcomes.Allowed,
                Code = null,
                BrokeredAppId = session.AppId,
            });
            return SessionRegistrationResult.Ok(session, secret);
        }
    }

    private SessionRegistrationResult RegisterOperator(RegisterRequest request, DateTimeOffset now)
    {
        if (_options.OperatorAppId is null || !_registry.TryGet(_options.OperatorAppId, out var operatorApp))
        {
            return SessionRegistrationResult.Deny(
                AetherErrorCodes.AuthOperatorTokenInvalid,
                "Operator identity is not configured.");
        }

        if (!TokenGenerator.FixedTimeEquals(request.RegistrationToken ?? "", _operatorToken))
        {
            AuditRegistrationDenied(request, AetherErrorCodes.AuthOperatorTokenInvalid);
            return SessionRegistrationResult.Deny(
                AetherErrorCodes.AuthOperatorTokenInvalid,
                "The operator token is invalid.");
        }

        var observed = _identityResolver.Resolve(request.ProcessId);
        if (observed is null)
        {
            AuditRegistrationDenied(request, AetherErrorCodes.AuthIdentityMismatch);
            return SessionRegistrationResult.Deny(
                AetherErrorCodes.AuthIdentityMismatch,
                "The calling process could not be verified.");
        }

        lock (_gate)
        {
            PruneExpiredLocked(now);
            if (_operatorTokenUses >= _options.OperatorTokenMaxUses)
            {
                AuditRegistrationDenied(request, AetherErrorCodes.AuthTokenInvalid);
                return SessionRegistrationResult.Deny(
                    AetherErrorCodes.AuthTokenInvalid,
                    "The operator token registration budget has been exhausted; restart the broker.");
            }

            _operatorTokenUses++;
            var instanceId = string.IsNullOrWhiteSpace(request.InstanceId) ? "shell-" + Guid.NewGuid().ToString("n")[..8] : request.InstanceId;
            var session = CreateSessionLocked(operatorApp, instanceId, observed, now, out var operatorSecret);
            _auditSink.Append(new AuditEventDraft
            {
                TraceId = TraceIdGenerator.NewTraceId(),
                EventType = AuditEventTypes.BrokerSession,
                Outcome = AuditOutcomes.Allowed,
                BrokeredAppId = session.AppId,
            });
            return SessionRegistrationResult.Ok(session, operatorSecret);
        }
    }

    public bool TryValidateSession(string? sessionId, string? secret, DateTimeOffset now, out BrokerSession session)
    {
        session = null!;
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(secret))
        {
            return false;
        }

        BrokerSession? candidate;
        lock (_gate)
        {
            PruneExpiredLocked(now);
            _sessions.TryGetValue(sessionId, out candidate);
        }

        if (candidate is null || candidate.IsExpired(now))
        {
            return false;
        }

        var computedHash = TokenGenerator.Hash(secret);
        if (!TokenGenerator.FixedTimeEquals(computedHash, candidate.SecretHash))
        {
            return false;
        }

        session = candidate;
        return true;
    }

    public void RecordHeartbeat(BrokerSession session, DateTimeOffset now)
    {
        if (session is not null)
        {
            session.TouchHeartbeat(now);
        }
    }

    public bool RevokeInstance(string instanceId)
    {
        lock (_gate)
        {
            if (!_byInstance.Remove(instanceId, out var session))
            {
                return false;
            }

            _sessions.Remove(session.SessionId);
            _auditSink.Append(new AuditEventDraft
            {
                TraceId = TraceIdGenerator.NewTraceId(),
                EventType = AuditEventTypes.BrokerSession,
                Outcome = AuditOutcomes.Info,
                BrokeredAppId = session.AppId,
            });
            return true;
        }
    }

    private BrokerSession CreateSessionLocked(
        RegisteredApp app,
        string instanceId,
        ProcessIdentity identity,
        DateTimeOffset now,
        out string secret)
    {
        secret = TokenGenerator.NewToken();
        var session = new BrokerSession(
            "sess-" + Guid.NewGuid().ToString("n"),
            app,
            instanceId,
            identity.ProcessId,
            identity.StartTimeUtc,
            TokenGenerator.Hash(secret),
            now,
            now.AddMinutes(_options.SessionTtlMinutes));
        _sessions[session.SessionId] = session;
        _byInstance[instanceId] = session;
        return session;
    }

    private void CompleteWaiterLocked(string instanceId, BrokerSession session)
    {
        if (_registrationWaiters.TryGetValue(instanceId, out var waiter))
        {
            waiter.TrySetResult(session);
        }
    }

    private void PruneExpiredLocked(DateTimeOffset now)
    {
        foreach (var (sessionId, session) in _sessions.ToArray())
        {
            if (session.IsExpired(now))
            {
                _sessions.Remove(sessionId);
                _byInstance.Remove(session.InstanceId);
            }
        }
    }

    private void PrunePendingLocked(DateTimeOffset now)
    {
        foreach (var (tokenHash, pending) in _pendingByTokenHash.ToArray())
        {
            if (pending.ExpiresAtUtc <= now)
            {
                _pendingByTokenHash.Remove(tokenHash);
                _pendingByInstance.Remove(pending.InstanceId);
                if (_registrationWaiters.TryGetValue(pending.InstanceId, out var waiter))
                {
                    waiter.TrySetResult(null);
                    _registrationWaiters.Remove(pending.InstanceId);
                }
            }
        }
    }

    private void AuditRegistrationDenied(RegisterRequest request, string code)
    {
        _auditSink.Append(new AuditEventDraft
        {
            TraceId = TraceIdGenerator.NewTraceId(),
            EventType = AuditEventTypes.BrokerSession,
            Outcome = AuditOutcomes.Denied,
            Code = code,
            BrokeredAppId = request.AppId,
        });
    }

    private static bool IsSupportedProtocol(string? protocolVersion)
    {
        if (string.IsNullOrWhiteSpace(protocolVersion))
        {
            return false;
        }

        return int.TryParse(protocolVersion, out var major) && major == AetherVersions.ProtocolVersionMajor;
    }

    internal static class TokenGenerator
    {
        public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        public static string Hash(string value)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(hash);
        }

        public static bool FixedTimeEquals(string left, string right)
        {
            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
            {
                return false;
            }

            var leftBytes = Encoding.UTF8.GetBytes(left);
            var rightBytes = Encoding.UTF8.GetBytes(right);
            return leftBytes.Length == rightBytes.Length && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }
    }
}
