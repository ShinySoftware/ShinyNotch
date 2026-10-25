using Microsoft.Maui.Controls.Shapes;
using ShinyNotch.Services;

namespace ShinyNotch.Views;

/// <summary>The shelf: a drop target when empty, otherwise the files as tiles with their actions.</summary>
/// <remarks>
/// While an app is being dragged in, a second target slides in beside the tray's to trust the app (see
/// <see cref="Platform.IFileActions.RemoveQuarantineAsync"/>). The drop lands on the window as a whole, so
/// the controller asks <see cref="IsOverTrust"/> where it fell.
/// </remarks>
sealed class TrayTab : VerticalStackLayout
{
    const int Columns = 6;
    const int MaxShown = 12;
    const double DropHeight = 112;

    readonly Border? trayTarget;
    readonly Border? trustTarget;

    public TrayTab(FileTrayService tray, bool appDrag = false)
    {
        this.Spacing = 10;
        var files = tray.Files;

        if (tray.Notice is { } notice)
            this.Children.Add(Notice(tray, notice));

        if (appDrag)
        {
            this.trayTarget = DropTarget("tray.and.arrow.down.fill", Ui.Teal, "Add to the tray", "keep it here to open or copy");
            this.trustTarget = DropTarget("checkmark.shield.fill", Ui.Green, "Remove Quarantine", "trust it, so macOS lets it open");
            this.trustTarget.Opacity = 0;
            this.trustTarget.TranslationX = 40;
            this.trustTarget.Loaded += (_, _) =>
            {
                _ = this.trustTarget.FadeToAsync(1, 220, Easing.CubicOut);
                _ = this.trustTarget.TranslateToAsync(0, 0, 220, Easing.CubicOut);
            };
            this.Children.Add(new Grid
            {
                ColumnSpacing = 10,
                ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star)],
                Children = { this.trayTarget.At(column: 0), this.trustTarget.At(column: 1) }
            });
            return;
        }

        if (files.Count == 0)
        {
            this.Children.Add(new Border
            {
                Stroke = Ui.Separator,
                StrokeThickness = 1.5,
                StrokeDashArray = [5, 4],
                StrokeShape = new RoundRectangle { CornerRadius = Ui.CardRadius },
                BackgroundColor = Ui.Surface.WithAlpha(0.5f),
                HeightRequest = DropHeight,
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

    /// <summary>The drag is over the trust target, at <paramref name="point"/> in the window. False when there is none.</summary>
    public bool IsOverTrust(Point point)
        => this.trustTarget != null && WindowBounds(this.trustTarget).Contains(point);

    /// <summary>Lights up the target the drag is over.</summary>
    public void DragMoved(Point point)
    {
        if (this.trayTarget == null || this.trustTarget == null)
            return;
        var trust = this.IsOverTrust(point);
        Light(this.trustTarget, Ui.Green, trust);
        Light(this.trayTarget, Ui.Teal, !trust);
    }

    static void Light(Border target, Color tint, bool on)
    {
        target.Stroke = on ? tint : Ui.Separator;
        target.BackgroundColor = on ? tint.WithAlpha(0.14f) : Ui.Surface.WithAlpha(0.5f);
    }

    /// <summary>Where a view is in its window: its bounds offset by every parent's, up to the page.</summary>
    static Rect WindowBounds(VisualElement view)
    {
        double x = 0, y = 0;
        for (Element? e = view; e is VisualElement v and not Page; e = e.Parent)
        {
            x += v.X + v.TranslationX;
            y += v.Y + v.TranslationY;
        }
        return new Rect(x, y, view.Width, view.Height);
    }

    static Border DropTarget(string symbol, Color tint, string title, string detail)
    {
        var target = new Border
        {
            Stroke = Ui.Separator,
            StrokeThickness = 1.5,
            StrokeDashArray = [5, 4],
            StrokeShape = new RoundRectangle { CornerRadius = Ui.CardRadius },
            BackgroundColor = Ui.Surface.WithAlpha(0.5f),
            HeightRequest = DropHeight,
            Content = new VerticalStackLayout
            {
                Spacing = 6,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    Centre(Ui.Symbol(symbol, 24, tint)),
                    Ui.Label(title, 13, bold: true).Centered(),
                    Ui.Label(detail, 11, color: Ui.Secondary).Centered()
                }
            }
        };
        return target;
    }

    static View Notice(FileTrayService tray, TrayNotice notice)
    {
        var (symbol, tint) = notice switch
        {
            { Working: true } => ("hourglass", Ui.Secondary),
            { Failed: true } => ("exclamationmark.triangle.fill", Ui.Orange),
            _ => ("checkmark.shield.fill", Ui.Green)
        };
        var message = Ui.Label(notice.Message, 12);
        message.MaxLines = 2;
        message.LineBreakMode = LineBreakMode.WordWrap;

        return Ui.Card(new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            Children =
            {
                Ui.Symbol(symbol, 13, tint).CenteredVertically().At(column: 0),
                message.At(column: 1),
                Ui.SymbolButton("xmark", tray.DismissNotice, 10, Ui.Tertiary, "Dismiss", padding: new Thickness(3)).CenteredVertically().At(column: 2)
            }
        }, new Thickness(10, 8));
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

        var tile = Ui.Card(new VerticalStackLayout { Spacing = 3, Children = { image, name, actions } }, new Thickness(4, 8, 4, 4), radius: 12);
        FlyoutBase.SetContextFlyout(tile, Menu(tray, path));
        return tile;
    }

    /// <summary>The tile's right-click menu: its buttons, and more that have no room on it.</summary>
    static MenuFlyout Menu(FileTrayService tray, string path)
    {
        static MenuFlyoutItem Item(string text, Action clicked)
            => new() { Text = text, Command = new Command(clicked) };

        var actions = tray.Actions;
        var menu = new MenuFlyout
        {
            Item("Open", () => actions.Open(path)),
            Item("Copy", () => actions.Copy([path])),
            Item($"Reveal in {actions.FileManagerName}", () => actions.Reveal([path]))
        };
        if (actions.ShareName is { } share)
            menu.Add(Item(share, () => actions.Share([path])));
        if (actions.CanRemoveQuarantine(path))
        {
            menu.Add(new MenuFlyoutSeparator());
            menu.Add(Item("Remove Quarantine Flags", () => _ = tray.RemoveQuarantineAsync(path)));
        }
        menu.Add(new MenuFlyoutSeparator());
        menu.Add(Item("Remove from Tray", () => tray.Remove(path)));
        return menu;
    }
}
