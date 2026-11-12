using ShinyNotch.Services;

namespace ShinyNotch.Views;

/// <summary>
/// The busiest of the user's processes: CPU, memory, disk and (where the platform can say) network, sorted
/// by whichever heading was clicked, each with a button to force quit it. The rows are built once and
/// re-filled every second, since the AppKit head can't add views to a layout already on screen. Force
/// quit takes two clicks: the first turns the row red and asks, the second kills it.
/// </summary>
sealed class ProcessesTab : VerticalStackLayout, ILiveView
{
    const int Rows = 10;
    const double RowHeight = 24;
    const double QuitSize = 9;
    static readonly TimeSpan ConfirmFor = TimeSpan.FromSeconds(3);

    readonly ProcessListService processes;
    readonly Label count;
    readonly List<(ProcessSort Sort, string Text, Label Label)> headings = new();
    readonly Row[] rows = new Row[Rows];
    int armedPid;
    int armedToken;

    sealed class Row
    {
        public int Pid;
        public required Label NameLabel;
        public required Label Cpu;
        public required Label Memory;
        public required Label Disk;
        public required Label? Network;
        public required Border Quit;
        public required Image QuitSymbol;
    }

    public ProcessesTab(ProcessListService processes)
    {
        this.processes = processes;
        this.Spacing = 8;

        this.count = Ui.Label("", 10, color: Ui.Tertiary);
        this.count.HorizontalOptions = LayoutOptions.End;
        this.Children.Add(new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            Children = { Ui.Caption("Your processes").At(column: 0), this.count.At(column: 1) }
        });

        var header = this.NewGrid();
        header.Children.Add(this.Heading("Name", ProcessSort.Name).At(column: 0));
        header.Children.Add(this.Heading("CPU", ProcessSort.Cpu).At(column: 1));
        header.Children.Add(this.Heading("Memory", ProcessSort.Memory).At(column: 2));
        header.Children.Add(this.Heading("Disk", ProcessSort.Disk).At(column: 3));
        if (processes.HasNetwork)
            header.Children.Add(this.Heading("Network", ProcessSort.Network).At(column: 4));

        var list = new VerticalStackLayout { Spacing = 0, Children = { header } };
        for (var i = 0; i < Rows; i++)
        {
            var row = this.NewRow();
            this.rows[i] = row;
            var grid = this.NewGrid();
            grid.HeightRequest = RowHeight;
            grid.Children.Add(row.NameLabel.At(column: 0));
            grid.Children.Add(row.Cpu.At(column: 1));
            grid.Children.Add(row.Memory.At(column: 2));
            grid.Children.Add(row.Disk.At(column: 3));
            if (row.Network != null)
                grid.Children.Add(row.Network.At(column: 4));
            grid.Children.Add(row.Quit.At(column: grid.ColumnDefinitions.Count - 1));
            list.Children.Add(grid);
        }
        this.Children.Add(Ui.Card(list, new Thickness(12, 6, 6, 6)));

        this.rows[0].NameLabel.Text = "Reading processes…";
        this.rows[0].NameLabel.TextColor = Ui.Tertiary;
        this.ShowHeadings();

        // The first sample is a baseline, unless the tab was open a moment ago; rates follow a second later.
        _ = this.RefreshAsync();
    }

    public void Tick() => _ = this.RefreshAsync();

    async Task RefreshAsync()
    {
        // While a force quit waits for its second click the rows hold still, so it stays under the pointer.
        if (await this.processes.SampleAsync() && this.armedPid == 0)
            this.Dispatcher.Dispatch(this.Show);
    }

    Grid NewGrid()
    {
        var grid = new Grid
        {
            ColumnSpacing = 6,
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(52),
                new ColumnDefinition(70),
                new ColumnDefinition(76)
            ]
        };
        if (this.processes.HasNetwork)
            grid.ColumnDefinitions.Add(new ColumnDefinition(76));
        grid.ColumnDefinitions.Add(new ColumnDefinition(26));
        return grid;
    }

    View Heading(string text, ProcessSort sort)
    {
        // Over the names, flush left like them; over the numbers, flush right.
        var isName = sort == ProcessSort.Name;
        var label = Ui.Label(text, 10, bold: true);
        label.HorizontalTextAlignment = isName ? TextAlignment.Start : TextAlignment.End;
        this.headings.Add((sort, text, label));

        var button = new Border
        {
            StrokeThickness = 0,
            BackgroundColor = Colors.Transparent,
            Padding = isName ? new Thickness(0, 3) : new Thickness(2, 3),
            Content = label
        };
        SemanticProperties.SetDescription(button, $"Sort by {text}");
        button.AutomationId = $"Sort by {text}";
        return button.Pressable(() =>
        {
            // The column it's already sorted by flips; another starts biggest first.
            this.processes.Reversed = this.processes.Sort == sort && !this.processes.Reversed;
            this.processes.Sort = sort;
            this.ShowHeadings();
            this.Show();
        });
    }

    Row NewRow()
    {
        Label Value()
        {
            var label = Ui.Label("", 11);
            label.HorizontalTextAlignment = TextAlignment.End;
            return label;
        }

        Row? row = null;
        // Its symbol is set once and never swapped: the AppKit head loads images asynchronously, and swaps
        // made close together can land out of order. It's hidden with the image's own opacity instead (the
        // button's is the click flash's).
        var quit = Ui.SymbolButton("xmark", () => this.QuitTapped(row!), QuitSize, Ui.Tertiary, "Force quit", padding: new Thickness(5));
        var symbol = (Image)quit.Content!;
        symbol.Opacity = 0;
        row = new Row
        {
            NameLabel = Ui.Label("", 12),
            Cpu = Value(),
            Memory = Value(),
            Disk = Value(),
            Network = this.processes.HasNetwork ? Value() : null,
            Quit = quit,
            QuitSymbol = symbol
        };
        return row;
    }

    void ShowHeadings()
    {
        // The column it's sorted by is marked, so the headings read as the buttons they are.
        foreach (var (sort, text, label) in this.headings)
        {
            var on = sort == this.processes.Sort;
            label.Text = on ? $"{text} {(this.processes.Ascending ? "▴" : "▾")}" : text;
            label.TextColor = on ? Ui.Teal : Ui.Secondary;
        }
    }

    void QuitTapped(Row row)
    {
        if (row.Pid == 0 || row.Pid == Environment.ProcessId)
            return;

        if (this.armedPid == row.Pid)
        {
            this.armedPid = 0;
            this.processes.ForceQuit(row.Pid);
            this.Show();
            return;
        }

        this.armedPid = row.Pid;
        var token = ++this.armedToken;
        this.Show();
        this.Dispatcher.DispatchDelayed(ConfirmFor, () =>
        {
            if (token != this.armedToken || this.armedPid == 0)
                return;
            this.armedPid = 0;
            this.Show();
        });
    }

    void Show()
    {
        var sort = this.processes.Sort;
        var top = this.processes.Top(Rows);
        this.count.Text = this.processes.Current is { Count: var n } ? $"{n} running" : "";

        for (var i = 0; i < Rows; i++)
        {
            var row = this.rows[i];
            if (i >= top.Count)
            {
                // Fewer processes than rows (or none readable yet): the row stays, empty.
                if (top.Count > 0 || this.processes.Current != null)
                    row.NameLabel.Text = "";
                row.Pid = 0;
                row.Cpu.Text = row.Memory.Text = row.Disk.Text = "";
                row.Network?.Text = "";
                row.QuitSymbol.Opacity = 0;
                continue;
            }

            var p = top[i];
            row.Pid = p.Pid;
            var armed = p.Pid == this.armedPid;
            row.NameLabel.Text = armed ? $"Force quit {p.Name}?" : p.Name;
            row.NameLabel.TextColor = armed ? Ui.Red : Ui.Text;

            row.Cpu.Text = $"{p.CpuPercent:0.0}";
            row.Memory.Text = SystemStatsService.Bytes(p.Memory);
            row.Disk.Text = PerSecond(p.DiskPerSec);
            row.Network?.Text = PerSecond(p.NetPerSec);

            row.Cpu.TextColor = sort == ProcessSort.Cpu ? Ui.Text : Ui.Secondary;
            row.Memory.TextColor = sort == ProcessSort.Memory ? Ui.Text : Ui.Secondary;
            row.Disk.TextColor = sort == ProcessSort.Disk ? Ui.Text : Ui.Secondary;
            row.Network?.TextColor = sort == ProcessSort.Network ? Ui.Text : Ui.Secondary;

            // This app can't quit itself from here; its row just has no button.
            row.QuitSymbol.Opacity = p.Pid == Environment.ProcessId ? 0 : 1;
        }
    }

    static string PerSecond(double bytes) => bytes < 1 ? "—" : $"{SystemStatsService.Bytes(bytes)}/s";
}
