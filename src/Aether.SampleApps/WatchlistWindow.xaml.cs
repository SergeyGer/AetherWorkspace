using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Aether.Contracts;
using Aether.Contracts.Synthetic;
using Aether.Contracts.Models.Contexts;
using Aether.Contracts.V1;

namespace Aether.SampleApps;

public partial class WatchlistWindow : Window
{
    private readonly SampleUiOptions _options;
    private SampleRuntime? _runtime;
    private WatchlistFixture? _fixture;
    private bool _suppressSelection;
    private string? _lastSelectionTicker;

    public WatchlistWindow(SampleUiOptions options)
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
            _fixture = Fixtures.LoadWatchlist(_options.FixturesDirectory);
            Grid.ItemsSource = _fixture.Instruments;
            _runtime = await SampleRuntime.ConnectAsync(_options, this);
            _runtime.ContextReceived += OnContextReceived;
            StatusText.Text = "Registered. Waiting for typed instrument context on the default channel…";
        }
        catch (Exception exception)
        {
            SampleDiagnostics.Log("connect-failed", exception);
            StatusText.Text = "Broker unavailable: " + exception.Message;
        }
    }

    private void OnContextReceived(InstrumentContext instrument, ContextEnvelope envelope)
    {
        StatusText.Text =
            $"Received: {instrument.Name} ({instrument.Id.Ticker}) | classification {envelope.Classification} | trace {envelope.TraceId}";
        var match = _fixture?.Instruments.FirstOrDefault(row => row.Ticker == instrument.Id.Ticker);
        if (match is not null)
        {
            _suppressSelection = true;
            try
            {
                Grid.SelectedItem = match;
                Grid.ScrollIntoView(match);
                _lastSelectionTicker = match.Ticker;
            }
            finally
            {
                _suppressSelection = false;
            }
        }
    }

    private async void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection || _runtime is null || Grid.SelectedItem is not WatchlistInstrument selected)
        {
            return;
        }

        if (string.Equals(_lastSelectionTicker, selected.Ticker, StringComparison.Ordinal))
        {
            return;
        }

        if (!DemoInstrumentCatalog.TryGet(selected.Ticker, out var instrument))
        {
            PublishStatusText.Text = $"No approved catalog context for {selected.Ticker}; nothing was published.";
            return;
        }

        _lastSelectionTicker = selected.Ticker;
        var traceId = TraceIdGenerator.NewTraceId();
        var clientMessageId = $"watchlist:{_runtime.InstanceId}:{instrument.Ticker}:{Guid.NewGuid():n}";
        try
        {
            PublishStatusText.Text = $"Publishing {instrument.Ticker}…";
            var reply = await _runtime.PublishContextAsync(instrument.ContextRef, instrument.Classification, traceId, clientMessageId);
            PublishStatusText.Text = reply.Accepted
                ? $"Published {instrument.Ticker} via {instrument.ContextRef} | classification {instrument.Classification} | delivered {reply.DeliveredCount} | trace {traceId}"
                : $"Publish denied for {instrument.Ticker}: {reply.DenialCode} {reply.DenialMessage}";
        }
        catch (Exception exception)
        {
            SampleDiagnostics.Log("publish-failed", exception, instrument.Ticker);
            PublishStatusText.Text = "Publish failed: " + exception.Message;
        }
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        if (_runtime is not null)
        {
            await _runtime.DisposeAsync();
        }
    }
}
