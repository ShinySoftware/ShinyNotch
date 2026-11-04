using ShinyNotch.Services;

namespace ShinyNotch.Views;

/// <summary>
/// The clipboard history: everything copied lately, latest first, with pinned entries above the rest.
/// Picking an entry puts it back on the clipboard, ready to paste; a pin keeps one for good, and a cross
/// forgets it. Clear forgets all but the pinned.
/// </summary>
sealed class ClipboardTab : VerticalStackLayout
{
    /// <summary>Rows shown before the list scrolls, so a long history doesn't push the panel off the screen.</summary>
    const int RowsBeforeScrolling = 7;
    const double RowHeight = 30;

    public ClipboardTab(ClipboardService clipboard, TimeProvider time)
    {
        this.Spacing = 8;
        var list = clipboard.Items;

        var header = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            Children =
            {
                Ui.Caption(list.Count switch
                {
                    0 => "Clipboard",
                    1 => "1 copied",
                    _ => $"{list.Count} copied"
                }).CenteredVertically().At(column: 0)
            }
        };
        if (list.Any(i => !i.Pinned))
            header.Children.Add(Ui.Chip("Clear", clipboard.Clear, symbol: "trash").At(column: 1));
        this.Children.Add(header);

        if (list.Count == 0)
        {
            this.Children.Add(Ui.Card(
                Ui.Label("Copy some text and it shows up here, ready to copy again.", 12, color: Ui.Secondary).Centered(),
                new Thickness(12, 20)));
            return;
        }

        var now = time.GetUtcNow();
        var current = clipboard.CurrentId;
        var rows = new VerticalStackLayout { Spacing = 0 };
        foreach (var item in list)
            rows.Children.Add(Row(clipboard, item, item.Id == current, now));

        View content = rows;
        if (list.Count > RowsBeforeScrolling)
            content = new ScrollView { Content = rows, HeightRequest = RowsBeforeScrolling * RowHeight };
        this.Children.Add(Ui.Card(content, new Thickness(6, 4)));

        this.Children.Add(Ui.Label("Pick an entry to put it back on the clipboard.", 10, color: Ui.Tertiary));
    }

    static View Row(ClipboardService clipboard, ClipboardItem item, bool current, DateTimeOffset now)
    {
        var text = Ui.Label(ClipboardService.Preview(item.Text), 13, bold: current, color: current ? Ui.Text : Ui.Secondary);
        var age = Ui.Label(Age(now - item.Copied), 10, color: Ui.Tertiary);
        age.HorizontalTextAlignment = TextAlignment.End;
        text.VerticalOptions = LayoutOptions.Center;
        age.VerticalOptions = LayoutOptions.Center;

        // The text is the button; the pin and the cross sit beside it rather than inside, so a click on
        // them never also picks the entry.
        var pick = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 7 },
            BackgroundColor = current ? Ui.Raised : Colors.Transparent,
            Padding = new Thickness(8, 0),
            Content = new Grid
            {
                ColumnSpacing = 8,
                ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
                Children = { text.At(column: 0), age.At(column: 1) }
            }
        }.Pressable(() => clipboard.Pick(item.Id));
        SemanticProperties.SetDescription(pick, $"Copy: {ClipboardService.Preview(item.Text, 60)}");

        return new Grid
        {
            HeightRequest = RowHeight,
            ColumnSpacing = 2,
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto)],
            Children =
            {
                pick.At(column: 0),
                Ui.SymbolButton(item.Pinned ? "pin.fill" : "pin", () => clipboard.TogglePin(item.Id), 11, item.Pinned ? Ui.Orange : Ui.Tertiary, item.Pinned ? "Unpin" : "Pin", padding: new Thickness(4)).At(column: 1),
                Ui.SymbolButton("xmark", () => clipboard.Delete(item.Id), 9, Ui.Tertiary, "Delete", padding: new Thickness(5)).At(column: 2)
            }
        };
    }

    static string Age(TimeSpan ago) => ago switch
    {
        { TotalMinutes: < 1 } => "now",
        { TotalHours: < 1 } => $"{(int)ago.TotalMinutes}m",
        { TotalDays: < 1 } => $"{(int)ago.TotalHours}h",
        _ => $"{(int)ago.TotalDays}d"
    };
}
