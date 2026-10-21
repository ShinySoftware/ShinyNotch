using ShinyNotch.Services;
using RoundRectangle = Microsoft.Maui.Controls.Shapes.RoundRectangle;

namespace ShinyNotch.Views;

/// <summary>
/// Voice notes: a record button with the time and the microphone's level, which microphone it is (tap for
/// the next one), then the latest notes to play, put in the tray, reveal or delete.
/// </summary>
sealed class NotesTab : VerticalStackLayout, ILiveView, IDisposable
{
    const int Shown = 4;

    readonly VoiceNotesService notes;
    readonly CapsuleDrawable meter = new() { Fill = Ui.Red };
    readonly GraphicsView meterView;
    readonly Label elapsed;

    public NotesTab(VoiceNotesService notes, FileTrayService tray)
    {
        this.notes = notes;
        this.Spacing = 10;

        var recording = notes.IsRecording;
        var record = Ui.SymbolButton(
            recording ? "stop.fill" : "mic.fill",
            () => _ = notes.ToggleRecordingAsync(),
            18,
            recording ? Colors.Black : Ui.Text,
            recording ? "Stop recording" : "Record a note",
            recording ? Ui.Red : Ui.Raised,
            new Thickness(10)
        );
        record.StrokeShape = new RoundRectangle { CornerRadius = 22 };

        this.elapsed = Ui.Label("", 20, bold: true);
        this.meterView = Drawing.View(this.meter, 200, 6);
        this.meterView.HorizontalOptions = LayoutOptions.Fill;

        var status = new VerticalStackLayout
        {
            Spacing = 6,
            VerticalOptions = LayoutOptions.Center,
            Children = { this.elapsed, this.meterView }
        };

        var top = new Grid
        {
            ColumnSpacing = 12,
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            Children = { record.At(column: 0), status.At(column: 1), Microphone(notes).At(column: 2) }
        };
        this.Children.Add(Ui.Card(top, new Thickness(12, 10)));

        if (notes.IsDenied)
            this.Children.Add(Ui.Label("Microphone access is off. Allow it in your system's privacy settings.", 12, color: Ui.Secondary));

        var list = notes.Notes;
        if (list.Count == 0)
        {
            if (!recording)
                this.Children.Add(Ui.Label("Tap the microphone to record a note.", 12, color: Ui.Secondary).Centered());
        }
        else
        {
            var rows = new VerticalStackLayout { Spacing = 2 };
            foreach (var note in list.Take(Shown))
                rows.Children.Add(Row(notes, tray, note));
            this.Children.Add(Ui.Card(rows, new Thickness(6, 4)));

            if (list.Count > Shown)
                this.Children.Add(Ui.Chip($"All {list.Count} notes", () => tray.Actions.Reveal([list[0].Path]), symbol: "folder.fill"));
        }

        this.notes.LevelChanged += this.OnLevel;
        this.Tick();
    }

    /// <summary>Which microphone: a chip that moves to the next one when there's a choice, else just its name.</summary>
    static View Microphone(VoiceNotesService notes)
    {
        var name = notes.Input?.Name ?? "Default microphone";
        if (notes.Inputs.Count < 2)
            return Ui.Label(name, 11, color: Ui.Secondary);

        var chip = Ui.Chip(Shorten(name), notes.NextInput, symbol: "mic");
        SemanticProperties.SetDescription(chip, $"Microphone: {name}. Tap for the next one.");
        return chip;
    }

    static string Shorten(string name) => name.Length > 22 ? name[..21] + "…" : name;

    static View Row(VoiceNotesService notes, FileTrayService tray, VoiceNote note)
    {
        var playing = notes.Playing == note.Path;
        return new Grid
        {
            ColumnSpacing = 6,
            Padding = new Thickness(0, 2),
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto)
            ],
            Children =
            {
                Ui.SymbolButton(playing ? "stop.fill" : "play.fill", () => _ = notes.TogglePlayAsync(note), 12, playing ? Ui.Red : Ui.Text, playing ? "Stop" : "Play").At(column: 0),
                Ui.Label(notes.Describe(note), 12, bold: playing).At(column: 1),
                Ui.Label(VoiceNotesService.Format(note.Duration), 11, color: Ui.Secondary).At(column: 2),
                new HorizontalStackLayout
                {
                    Spacing = 0,
                    Children =
                    {
                        Ui.SymbolButton("tray.and.arrow.down", () => tray.Add([note.Path]), 11, Ui.Secondary, "Put in the File Tray", padding: new Thickness(4)),
                        Ui.SymbolButton("magnifyingglass", () => tray.Actions.Reveal([note.Path]), 11, Ui.Secondary, $"Reveal in {tray.Actions.FileManagerName}", padding: new Thickness(4)),
                        Ui.SymbolButton("trash", () => _ = notes.DeleteAsync(note), 11, Ui.Secondary, "Delete", padding: new Thickness(4))
                    }
                }.At(column: 3)
            }
        };
    }

    void OnLevel(object? sender, EventArgs e)
    {
        this.meter.Value = this.notes.Level;
        this.meterView.Invalidate();
    }

    public void Tick()
    {
        var recording = this.notes.IsRecording;
        this.elapsed.Text = recording ? VoiceNotesService.Format(this.notes.Elapsed) : "0:00";
        this.elapsed.TextColor = recording ? Ui.Text : Ui.Tertiary;
        this.meter.Value = recording ? this.notes.Level : 0;
        this.meterView.Invalidate();
    }

    public void Dispose() => this.notes.LevelChanged -= this.OnLevel;
}
