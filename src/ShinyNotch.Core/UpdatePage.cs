using Shiny.Maui.Controls.Markdown;
using ShinyNotch.Services;
using ShinyNotch.Views;

namespace ShinyNotch;

/// <summary>
/// "A new version is available": the release's version and notes, with Install and Later. Shown in a
/// window of its own when a check finds a release, and from Settings' Update section.
/// </summary>
/// <remarks>
/// "Don't show again" skips this release only, and takes effect however the window closes (Later, Install
/// failing, the close button). The progress and the error line are on the page from the start and only
/// shown when needed: the AppKit head never realises views added to a page that is already on screen.
/// </remarks>
public sealed class UpdatePage : ContentPage
{
    readonly UpdateService updates;
    readonly AppRelease release;
    readonly CheckBox dontShow;
    readonly ProgressBar progress;
    readonly Label status;
    readonly Button install;
    readonly Button later;

    public UpdatePage(UpdateService updates, AppRelease release)
    {
        this.updates = updates;
        this.release = release;
        this.Title = "Shiny Notch Update";
        this.BackgroundColor = Ui.Surface;

        var header = new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                Ui.Label("A new version of Shiny Notch is available", 20, bold: true, color: Ui.Text),
                Ui.Label($"Version {release.DisplayVersion} - you have {updates.CurrentVersion}", 13, color: Ui.Secondary),
                Ui.Label($"Released {release.PublishedAt.LocalDateTime:D}", 12, color: Ui.Tertiary)
            }
        };

        var notes = new MarkdownView { Markdown = release.Notes.Length > 0 ? release.Notes : "_No release notes._" };
        var notesCard = Ui.Card(notes, new Thickness(14, 10), Ui.Raised);

        this.dontShow = new CheckBox { Color = Ui.Blue, VerticalOptions = LayoutOptions.Center };
        var dontShowLabel = Ui.Label("Don't show again", 13, color: Ui.Text).CenteredVertically();
        dontShowLabel.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => this.dontShow.IsChecked = !this.dontShow.IsChecked) });

        this.progress = new ProgressBar { ProgressColor = Ui.Blue, IsVisible = false };
        this.status = Ui.Label("", 12, color: Ui.Secondary);
        this.status.IsVisible = false;

        this.later = Action("Later", Ui.Raised, this.Close);
        this.install = Action("Install and Restart", Ui.Blue, async () => await this.InstallAsync());

        var footer = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto)],
            ColumnSpacing = 8,
            Children =
            {
                this.dontShow.At(column: 0),
                dontShowLabel.At(column: 1),
                this.later.At(column: 3),
                this.install.At(column: 4)
            }
        };

        var gitHub = Ui.Label("View on GitHub", 12, color: Ui.Blue);
        gitHub.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => _ = Launcher.OpenAsync(release.Page)) });

        this.Content = new Grid
        {
            Padding = new Thickness(24, 20),
            RowSpacing = 14,
            RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto)],
            Children =
            {
                header.At(row: 0),
                notesCard.At(row: 1),
                gitHub.At(row: 2),
                this.progress.At(row: 3),
                this.status.At(row: 4),
                footer.At(row: 5)
            }
        };
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (this.dontShow.IsChecked)
            this.updates.Skip(this.release);
    }

    async Task InstallAsync()
    {
        this.install.IsEnabled = this.later.IsEnabled = false;
        this.progress.Progress = 0;
        this.progress.IsVisible = true;
        this.status.TextColor = Ui.Secondary;
        this.status.Text = "Downloading…";
        this.status.IsVisible = true;
        try
        {
            await this.updates.InstallAsync(new Progress<double>(p =>
            {
                this.progress.Progress = p;
                if (p >= 1)
                    this.status.Text = "Installing…";
            }));
            // Installed: the app is quitting to come back as the new version.
        }
        catch (Exception ex)
        {
            this.progress.IsVisible = false;
            this.status.TextColor = Ui.Red;
            this.status.Text = $"Couldn't install the update: {ex.Message}";
            this.install.IsEnabled = this.later.IsEnabled = true;
        }
    }

    static Button Action(string text, Color background, Action tapped) => new()
    {
        Text = text,
        BackgroundColor = background,
        TextColor = Colors.White,
        CornerRadius = 8,
        HeightRequest = 32,
        Padding = new Thickness(16, 0),
        VerticalOptions = LayoutOptions.Center,
        Command = new Command(tapped)
    };

    void Close()
    {
        if (this.Window is { } window)
            Application.Current?.CloseWindow(window);
    }
}
