using System;
using System.Linq;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using Aether.Contracts.Models.Contexts;
using Aether.Contracts.V1;

namespace Aether.SampleApps;

public partial class ChartWindow : Window
{
    private readonly SampleUiOptions _options;
    private SampleRuntime? _runtime;
    private ChartFixture? _fixture;

    public ChartWindow(SampleUiOptions options)
    {
        InitializeComponent();
        _options = options;
        Loaded += OnLoaded;
        SizeChanged += (_, _) => DrawChart(null);
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _fixture = Fixtures.LoadChart(_options.FixturesDirectory);
            DrawChart(null);
            _runtime = await SampleRuntime.ConnectAsync(_options, this);
            _runtime.ContextReceived += OnContextReceived;
            StatusText.Text = "Registered. Waiting for typed instrument context on the default channel…";
        }
        catch (Exception exception)
        {
            StatusText.Text = "Broker unavailable: " + exception.Message;
        }
    }

    private void OnContextReceived(InstrumentContext instrument, ContextEnvelope envelope)
    {
        StatusText.Text =
            $"Chart synced to {instrument.Name} ({instrument.Id.Ticker}) | classification {envelope.Classification} | trace {envelope.TraceId}";
        DrawChart(instrument.Id.Ticker);
    }

    private void DrawChart(string? highlightedTicker)
    {
        if (_fixture is null || ChartCanvas.ActualWidth < 10 || ChartCanvas.ActualHeight < 10)
        {
            return;
        }

        ChartCanvas.Children.Clear();
        var series = _fixture.Series.FirstOrDefault(item => highlightedTicker is null || item.Ticker == highlightedTicker)
                     ?? _fixture.Series.FirstOrDefault();
        if (series is null || series.Points.Count < 2)
        {
            return;
        }

        var width = ChartCanvas.ActualWidth;
        var height = ChartCanvas.ActualHeight;
        var margin = 40.0;
        var values = series.Points.Select(point => (double)point.Value).ToArray();
        var min = values.Min();
        var max = values.Max();
        var span = Math.Max(0.0001, max - min);

        var polyline = new Polyline
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0x2F, 0x6F, 0xB5)),
            StrokeThickness = 2,
        };

        for (var index = 0; index < series.Points.Count; index++)
        {
            var x = margin + (index * ((width - (2 * margin)) / (series.Points.Count - 1)));
            var y = height - margin - (((values[index] - min) / span) * (height - (2 * margin)));
            polyline.Points.Add(new Point(x, y));
        }

        ChartCanvas.Children.Add(polyline);
        ChartCanvas.Children.Add(new TextBlock
        {
            Text = series.Label,
            Margin = new Thickness(margin, 8, 0, 0),
            FontWeight = FontWeights.SemiBold,
        });
        ChartCanvas.Children.Add(new TextBlock
        {
            Text = $"{series.Points[0].Month} … {series.Points[^1].Month}",
            Margin = new Thickness(margin, height - 24, 0, 0),
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x6B)),
        });
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        if (_runtime is not null)
        {
            await _runtime.DisposeAsync();
        }
    }
}
