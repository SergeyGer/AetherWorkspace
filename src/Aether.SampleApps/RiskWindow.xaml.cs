using System;
using System.Linq;
using System.Globalization;
using System.Windows;
using Aether.Contracts.Models.Contexts;
using Aether.Contracts.V1;

namespace Aether.SampleApps;

public partial class RiskWindow : Window
{
    private readonly SampleUiOptions _options;
    private SampleRuntime? _runtime;
    private RiskFixture? _fixture;

    public RiskWindow(SampleUiOptions options)
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
            _fixture = Fixtures.LoadRisk(_options.FixturesDirectory);
            Grid.ItemsSource = _fixture.Positions;
            GrossText.Text = "Gross notional: " + _fixture.Positions.Sum(position => position.Notional).ToString("N0", CultureInfo.InvariantCulture) + " " + _fixture.Currency;
            NetText.Text = "Net market value: " + _fixture.Positions.Sum(position => position.MarketValue).ToString("N0", CultureInfo.InvariantCulture);
            Dv01Text.Text = "Total DV01: " + _fixture.Positions.Sum(position => position.Dv01).ToString("N0", CultureInfo.InvariantCulture);
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
            $"Risk view synced to {instrument.Name} ({instrument.Id.Ticker}) | classification {envelope.Classification} | trace {envelope.TraceId}";
        var match = _fixture?.Positions.FirstOrDefault(position => position.Ticker == instrument.Id.Ticker);
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
