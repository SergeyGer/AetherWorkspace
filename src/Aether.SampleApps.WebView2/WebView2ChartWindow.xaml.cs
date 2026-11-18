using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using Aether.Contracts.Fdc3;
using Aether.Contracts.Models.Contexts;
using Aether.Contracts.V1;
using Microsoft.Web.WebView2.Core;

namespace Aether.SampleApps.WebView2;

/// <summary>
/// v1.2 AW-057/AW-058: WPF host around a local WebView2 chart. The app is a separate
/// process launched by the broker; approved <c>fdc3.instrument</c> contexts arrive through
/// <see cref="SampleRuntime"/>, are projected to a minimal allowlisted JSON message and are
/// posted to the local JavaScript renderer. The JS renderer acknowledges the render back to
/// .NET so the WPF automation surface proves the host→JS round trip.
/// </summary>
public partial class WebView2ChartWindow : Window
{
    private readonly SampleUiOptions _options;
    private SampleRuntime? _runtime;
    private CoreWebView2? _core;
    private ChartFixture? _fixture;
    private (InstrumentContext Instrument, ContextEnvelope Envelope)? _pendingContext;
    private bool _jsReady;

    public WebView2ChartWindow(SampleUiOptions options)
    {
        InitializeComponent();
        _options = options;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _fixture = Fixtures.LoadChart(_options.FixturesDirectory);
            _runtime = await SampleRuntime.ConnectAsync(_options, this);
            ProcessBadgeText.Text = $"PID {_runtime.ProcessId} · {_runtime.DisplayAppId} · {_runtime.TransportLabel}";
            _runtime.ContextReceived += OnContextReceived;
            StatusText.Text = "Registered with broker. Initializing local WebView2 renderer…";
            await InitializeWebViewAsync();
        }
        catch (Exception exception)
        {
            SampleDiagnostics.Log("connect-or-webview-failed", exception);
            ShowFallback("WebView2 chart initialization failed: " + exception.Message);
        }
    }

    private async System.Threading.Tasks.Task InitializeWebViewAsync()
    {
        try
        {
            if (string.Equals(Environment.GetEnvironmentVariable("AETHER_WEBVIEW2_FORCE_FALLBACK"), "1", StringComparison.Ordinal))
            {
                ShowFallback(
                    "WebView2 Runtime fallback path (forced by AETHER_WEBVIEW2_FORCE_FALLBACK=1). "
                    + "On a real machine this is shown when the Microsoft Edge WebView2 Evergreen Runtime is missing; "
                    + "install it from https://developer.microsoft.com/microsoft-edge/webview2/ and relaunch.");
                return;
            }

            var userDataFolder = Path.Combine(Path.GetTempPath(), "aether-webview2-" + Environment.ProcessId);
            Directory.CreateDirectory(userDataFolder);
            var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await WebView2Surface.EnsureCoreWebView2Async(environment);

            _core = WebView2Surface.CoreWebView2;
            _core.Settings.AreDevToolsEnabled = false;
            _core.Settings.AreDefaultContextMenusEnabled = false;
            _core.Settings.IsStatusBarEnabled = false;
            _core.Settings.IsZoomControlEnabled = false;
            _core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            _core.Settings.AreHostObjectsAllowed = false;
            _core.WebMessageReceived += OnWebMessageReceived;

            var chartPage = Path.Combine(AppContext.BaseDirectory, "chart.html");
            if (!File.Exists(chartPage))
            {
                throw new FileNotFoundException("The local WebView2 chart page is missing from the build output.", chartPage);
            }

            // A local virtual host keeps the page same-origin for its local CSS/JS under a strict
            // CSP and still performs no network access (DenyCors; the folder mapping is local-only).
            _core.SetVirtualHostNameToFolderMapping(
                "aether.local",
                AppContext.BaseDirectory,
                CoreWebView2HostResourceAccessKind.DenyCors);
            _core.Navigate("https://aether.local/chart.html");
            StatusText.Text = $"WebView2 renderer PID {_runtime?.ProcessId.ToString() ?? "?"} · waiting for JS bridge";
            FallbackText.Visibility = Visibility.Collapsed;
        }
        catch (WebView2RuntimeNotFoundException exception)
        {
            SampleDiagnostics.Log("webview2-runtime-missing", exception);
            ShowFallback(
                "Microsoft Edge WebView2 Runtime is not installed. Install the Evergreen WebView2 Runtime "
                + "(https://developer.microsoft.com/microsoft-edge/webview2/) and relaunch. The rest of the "
                + "workspace keeps running; this chart sample intentionally falls back without a renderer.");
        }
        catch (Exception exception)
        {
            SampleDiagnostics.Log("webview2-init-failed", exception);
            ShowFallback("WebView2 renderer failed to initialize: " + exception.Message);
        }
    }

    private void ShowFallback(string message)
    {
        FallbackText.Text = message;
        FallbackText.Visibility = Visibility.Visible;
        StatusText.Text = "WebView2 chart unavailable (fallback active).";
        BridgeStatusText.Text = "fallback: no JS renderer in this session";
    }

    private void OnContextReceived(InstrumentContext instrument, ContextEnvelope envelope)
    {
        if (_core is null || !_jsReady)
        {
            _pendingContext = (instrument, envelope);
            BridgeStatusText.Text = $"context queued: {instrument.Id.Ticker} (JS bridge starting)";
            return;
        }

        PostContext(instrument, envelope);
    }

    private void PostContext(InstrumentContext instrument, ContextEnvelope envelope)
    {
        try
        {
            var series = _fixture?.Series.FirstOrDefault(item =>
                string.Equals(item.Ticker, instrument.Id.Ticker, StringComparison.Ordinal));
            if (series is null)
            {
                BridgeStatusText.Text = $"rejected: no local series for {instrument.Id.Ticker}";
                return;
            }

            var points = series.Points
                .Select(point => new WebView2BridgePoint { Month = point.Month, Value = point.Value })
                .ToArray();
            var json = WebView2ContextBridge.SerializeInstrument(instrument, envelope, points);
            _core!.PostWebMessageAsJson(json);
            StatusText.Text = $"Context sent to JS renderer: {instrument.Id.Ticker} ({envelope.Classification})";
        }
        catch (Exception exception)
        {
            SampleDiagnostics.Log("bridge-post-failed", exception);
            BridgeStatusText.Text = "rejected: " + exception.Message;
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var root = document.RootElement;
            var type = root.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
            switch (type)
            {
                case "bridge-ready":
                    _jsReady = true;
                    BridgeStatusText.Text = "JS bridge ready · waiting for approved typed context";
                    StatusText.Text = "WebView2 renderer ready; local JS chart loaded.";
                    if (_pendingContext is { } pending)
                    {
                        _pendingContext = null;
                        PostContext(pending.Instrument, pending.Envelope);
                    }

                    break;
                case "bridge-ack":
                    var ticker = root.GetProperty("ticker").GetString();
                    var traceId = root.GetProperty("traceId").GetString();
                    var classification = root.GetProperty("classification").GetString();
                    var pointCount = root.GetProperty("points").GetInt32();
                    BridgeStatusText.Text = $"ack {ticker} | classification {classification} | trace {traceId} | JS points {pointCount}";
                    StatusText.Text = "JS chart rendered the broker-approved typed context.";
                    break;
                case "bridge-reject":
                    var reason = root.TryGetProperty("reason", out var reasonElement) ? reasonElement.GetString() : "unspecified";
                    BridgeStatusText.Text = "rejected by JS bridge: " + reason;
                    break;
                default:
                    BridgeStatusText.Text = "ignored unknown JS message";
                    break;
            }
        }
        catch (Exception exception)
        {
            SampleDiagnostics.Log("bridge-message-failed", exception);
            BridgeStatusText.Text = "rejected: malformed JS message";
        }
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        if (_runtime is not null)
        {
            await _runtime.DisposeAsync();
        }

        try
        {
            WebView2Surface.Dispose();
        }
        catch (Exception exception) when (exception is ObjectDisposedException or InvalidOperationException)
        {
        }
    }
}
