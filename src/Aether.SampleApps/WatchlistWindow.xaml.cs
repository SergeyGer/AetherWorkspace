using System;
using System.Linq;
using System.Windows;
using Aether.Contracts.Models.Contexts;
using Aether.Contracts.V1;

namespace Aether.SampleApps;

public partial class WatchlistWindow : Window
{
    private readonly SampleUiOptions _options;
    private SampleRuntime? _runtime;
    private WatchlistFixture? _fixture;

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
            $"Context: {instrument.Name} ({instrument.Id.Ticker}) | classification {envelope.Classification} | trace {envelope.TraceId}";
        var match = _fixture?.Instruments.FirstOrDefault(row => row.Ticker == instrument.Id.Ticker);
        if (match is not null)
        {
            Grid.SelectedItem = match;
            Grid.ScrollIntoView(match);
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
