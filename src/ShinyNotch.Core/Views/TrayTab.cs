using Microsoft.Maui.Controls.Shapes;
using ShinyNotch.Services;

namespace ShinyNotch.Views;

/// <summary>The shelf: a drop target when empty, otherwise the files as tiles with their actions.</summary>
sealed class TrayTab : VerticalStackLayout
{
    const int Columns = 6;
    const int MaxShown = 12;

    public TrayTab(FileTrayService tray)
    {
        this.Spacing = 10;
        var files = tray.Files;

        if (files.Count == 0)
        {
            this.Children.Add(new Border
            {
                Stroke = Ui.Separator,
                StrokeThickness = 1.5,
                StrokeDashArray = [5, 4],
                StrokeShape = new RoundRectangle { CornerRadius = Ui.CardRadius },
                BackgroundColor = Ui.Surface.WithAlpha(0.5f),
                HeightRequest = 112,
                Content = new VerticalStackLayout
                {
                    Spacing = 6,
                    VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                        Centre(Ui.Symbol("tray.and.arrow.down.fill", 24, Ui.Teal)),
                        Ui.Label("Drop files on the notch", 13, bold: true).Centered(),
                        Ui.Label(tray.Actions.ShareName is { } share ? $"then copy, {share} or reveal them from here" : "then copy or reveal them from here", 11, color: Ui.Secondary).Centered()
                    }
                }
            });
            return;
        }

        var grid = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        for (var c = 0; c < Columns; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        var shown = files.Take(MaxShown).ToList();
        for (var i = 0; i < shown.Count; i++)
        {
            if (i % Columns == 0)
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.Children.Add(Tile(tray, shown[i]).At(row: i / Columns, column: i % Columns));
        }
        this.Children.Add(grid);

        var buttons = new HorizontalStackLayout { Spacing = 6 };
        buttons.Children.Add(Ui.Chip("Copy", () => tray.Actions.Copy(tray.Files), selected: true, symbol: "doc.on.doc.fill"));
        if (tray.Actions.ShareName is { } shareName)
            buttons.Children.Add(Ui.Chip(shareName, () => tray.Actions.Share(tray.Files), symbol: tray.Actions.ShareSymbol));
        buttons.Children.Add(Ui.Chip("Reveal", () => tray.Actions.Reveal(tray.Files), symbol: "folder.fill"));
        buttons.Children.Add(Ui.Chip("Clear", tray.Clear, tint: Ui.Red, symbol: "trash.fill"));

        var count = files.Count == 1 ? "1 file" : $"{files.Count} files";
        var actions = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            Children =
            {
                Ui.Label(files.Count > MaxShown ? $"{count} · {files.Count - MaxShown} not shown" : count, 11, color: Ui.Secondary).At(column: 0),
                buttons.At(column: 1)
            }
        };
        this.Children.Add(actions);
    }

    static View Centre(View view)
    {
        view.HorizontalOptions = LayoutOptions.Center;
        return view;
    }

    static View Tile(FileTrayService tray, string path)
    {
        var icon = Ui.Platform.FileIcon(path, 64);
        var image = new Image
        {
            Source = icon == null ? null : ImageSource.FromStream(() => new MemoryStream(icon)),
            WidthRequest = 40,
            HeightRequest = 40,
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Center
        };
        image.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => tray.Actions.Open(path)) });

        var name = Ui.Label(System.IO.Path.GetFileName(path), 10, color: Ui.Secondary, width: 60);
        name.HorizontalOptions = LayoutOptions.Center;
        name.HorizontalTextAlignment = TextAlignment.Center;
        name.LineBreakMode = LineBreakMode.MiddleTruncation;

        var actions = new HorizontalStackLayout
        {
            Spacing = 0,
            HorizontalOptions = LayoutOptions.Center,
            Children =
            {
                Ui.SymbolButton("doc.on.doc", () => tray.Actions.Copy([path]), 10, Ui.Secondary, "Copy", padding: new Thickness(3)),
                Ui.SymbolButton("magnifyingglass", () => tray.Actions.Reveal([path]), 10, Ui.Secondary, $"Reveal in {tray.Actions.FileManagerName}", padding: new Thickness(3)),
                Ui.SymbolButton("xmark", () => tray.Remove(path), 10, Ui.Tertiary, "Remove", padding: new Thickness(3))
            }
        };

        return Ui.Card(new VerticalStackLayout { Spacing = 3, Children = { image, name, actions } }, new Thickness(4, 8, 4, 4), radius: 12);
    }
}
