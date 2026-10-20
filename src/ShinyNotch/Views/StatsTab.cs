using ShinyNotch.Services;

namespace ShinyNotch.Views;

/// <summary>
/// System: CPU, GPU and memory as rings, recent CPU as a chart, then network and disk rates. Sampled once
/// a second, and only while this tab is the one on screen.
/// </summary>
sealed class StatsTab : VerticalStackLayout, ILiveView
{
    const double RingSize = 46;

    readonly SystemStatsService stats;
    readonly Gauge cpu, gpu, memory;
    readonly SparkDrawable spark = new() { Stroke = Ui.Teal };
    readonly GraphicsView sparkView;
    readonly Label cpuNow, netIn, netOut, diskRead, diskWrite;

    sealed record Gauge(RingDrawable Ring, GraphicsView View, Label Value, Label Detail);

    public StatsTab(SystemStatsService stats)
    {
        this.stats = stats;
        this.Spacing = 10;

        Gauge NewGauge(string name, Color color, out View card)
        {
            var ring = new RingDrawable { Fill = color, Thickness = 5 };
            var view = Drawing.View(ring, RingSize, RingSize);
            var value = Ui.Label("—", 17, bold: true);
            var detail = Ui.Label("", 10, color: Ui.Tertiary);
            card = Ui.Card(new HorizontalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    view,
                    new VerticalStackLayout { Spacing = 0, VerticalOptions = LayoutOptions.Center, Children = { Ui.Caption(name, color), value, detail } }
                }
            }, new Thickness(10));
            return new Gauge(ring, view, value, detail);
        }

        this.cpu = NewGauge("CPU", Ui.Teal, out var cpuCard);
        this.gpu = NewGauge("GPU", Ui.Purple, out var gpuCard);
        this.memory = NewGauge("Memory", Ui.Green, out var memoryCard);
        this.Children.Add(new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star)],
            Children = { cpuCard.At(column: 0), gpuCard.At(column: 1), memoryCard.At(column: 2) }
        });

        this.cpuNow = Ui.Label("", 10, bold: true, color: Ui.Teal);
        this.cpuNow.HorizontalOptions = LayoutOptions.End;
        this.sparkView = Drawing.View(this.spark, Ui.ContentWidth - 24, 40);
        this.sparkView.HorizontalOptions = LayoutOptions.Fill;
        this.Children.Add(Ui.Card(new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                new Grid
                {
                    ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
                    Children = { Ui.Caption("CPU · last 24 seconds").At(column: 0), this.cpuNow.At(column: 1) }
                },
                this.sparkView
            }
        }));

        View Rate(string symbol, Color color, out Label value)
        {
            value = Ui.Label("—", 12, bold: true);
            return new HorizontalStackLayout { Spacing = 6, Children = { Ui.Symbol(symbol, 12, color), value } };
        }

        View Pair(string caption, View first, View second)
            => Ui.Card(new VerticalStackLayout
            {
                Spacing = 4,
                Children = { Ui.Caption(caption), new HorizontalStackLayout { Spacing = 18, Children = { first, second } } }
            });

        this.Children.Add(new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star)],
            Children =
            {
                Pair("Network", Rate("arrow.down.circle.fill", Ui.Blue, out this.netIn), Rate("arrow.up.circle.fill", Ui.Pink, out this.netOut)).At(column: 0),
                Pair("Disk", Rate("arrow.down.doc.fill", Ui.Orange, out this.diskRead), Rate("arrow.up.doc.fill", Ui.Yellow, out this.diskWrite)).At(column: 1)
            }
        });

        // No sample here: Begin takes the baseline, and the first rates need a second of counters after it.
        stats.Begin();
        if (stats.Current is { } resumed)
            this.Show(resumed);
    }

    public void Tick() => this.Show(this.stats.Sample());

    void Show(SystemStats s)
    {
        static void Set(Gauge gauge, double? percent, string detail)
        {
            gauge.Ring.Value = (percent ?? 0) / 100;
            gauge.View.Invalidate();
            gauge.Value.Text = percent is { } p ? $"{p:0}%" : "—";
            gauge.Detail.Text = detail;
        }

        Set(this.cpu, s.CpuPercent, "in use");
        Set(this.gpu, s.GpuPercent, s.GpuPercent == null ? "not reported" : "in use");
        Set(this.memory, s.MemoryPercent, $"{SystemStatsService.Bytes(s.MemoryUsed)} of {SystemStatsService.Bytes(s.MemoryTotal)}");

        this.spark.Values = this.stats.CpuHistory;
        this.sparkView.Invalidate();
        this.cpuNow.Text = $"{s.CpuPercent:0}%";

        this.netIn.Text = $"{SystemStatsService.Bytes(s.NetInPerSec)}/s";
        this.netOut.Text = $"{SystemStatsService.Bytes(s.NetOutPerSec)}/s";
        this.diskRead.Text = $"{SystemStatsService.Bytes(s.DiskReadPerSec)}/s";
        this.diskWrite.Text = $"{SystemStatsService.Bytes(s.DiskWritePerSec)}/s";
    }
}
