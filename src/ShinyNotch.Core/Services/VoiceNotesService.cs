using Shiny;
using Shiny.Audio;

namespace ShinyNotch.Services;

/// <param name="Path">The WAV file. Its name is the moment it was recorded.</param>
public sealed record VoiceNote(string Path, DateTime Recorded, TimeSpan Duration);

/// <summary>
/// Voice notes: recorded from the chosen microphone to WAV files in the app's own folder, listed newest
/// first, played back one at a time.
/// </summary>
/// <remarks>
/// The folder is the list - nothing else is stored - so a note deleted or added in the file manager is
/// simply there or not the next time the tab opens.
/// </remarks>
[Singleton]
public sealed class VoiceNotesService
{
    /// <summary>Shiny.Audio records 16 kHz, 16-bit mono: the bytes per second of a note, for its length.</summary>
    const int BytesPerSecond = 16000 * 2;
    const int WavHeader = 44;
    const string FileFormat = "yyyy-MM-dd HH.mm.ss";

    readonly IAudioRecorder recorder;
    readonly IAudioPlayer player;
    readonly IAudioDevices? devices;
    readonly AppSettings settings;
    readonly IDispatcher dispatcher;
    readonly TimeProvider time;
    IAudioPlayback? playback;
    /// <summary>The file being recorded, kept out of the list until it is finished.</summary>
    string? recording;

    public VoiceNotesService(IAudioRecorder recorder, IAudioPlayer player, IEnumerable<IAudioDevices> devices, AppSettings settings, IDispatcher dispatcher, TimeProvider time)
    {
        this.recorder = recorder;
        this.player = player;
        // Not every platform can list microphones; without it, notes record from the default one.
        this.devices = devices.FirstOrDefault();
        this.settings = settings;
        this.dispatcher = dispatcher;
        this.time = time;

        this.Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Shiny Notch", "Voice Notes");
        this.recorder.InputLevelChanged += (_, level) => this.dispatcher.Dispatch(() =>
        {
            this.Level = level;
            this.LevelChanged?.Invoke(this, EventArgs.Empty);
        });
        if (this.devices != null)
            this.devices.Changed += (_, _) => this.dispatcher.Dispatch(() => this.Changed?.Invoke(this, EventArgs.Empty));
    }

    public string Folder { get; }

    public bool IsRecording => this.recorder.IsRecording;

    public TimeSpan Elapsed => this.recorder.Elapsed;

    /// <summary>The microphone level while recording, 0..1.</summary>
    public double Level { get; private set; }

    /// <summary>The note playing now, or null.</summary>
    public string? Playing { get; private set; }

    /// <summary>The microphone was turned off for the app in the system's privacy settings.</summary>
    public bool IsDenied { get; private set; }

    /// <summary>Recording started or stopped, a note was added, deleted or finished playing, or the microphones changed.</summary>
    public event EventHandler? Changed;

    /// <summary>The level moved. Only redraws the meter, so it doesn't rebuild the tab.</summary>
    public event EventHandler? LevelChanged;

    /// <summary>Microphones to choose from, or none when the platform can't say (then the default is used).</summary>
    public IReadOnlyList<AudioDevice> Inputs => this.devices?.GetInputs() ?? [];

    /// <summary>The microphone notes record from: the one chosen, while it is still there, else the default.</summary>
    public AudioDevice? Input
    {
        get
        {
            var inputs = this.Inputs;
            return inputs.FirstOrDefault(d => d.Id == this.settings.VoiceNoteInput) ?? inputs.FirstOrDefault(d => d.IsCurrent);
        }
    }

    /// <summary>Moves to the next microphone. Takes effect from the next recording.</summary>
    public void NextInput()
    {
        var inputs = this.Inputs;
        if (inputs.Count < 2)
            return;

        var i = inputs.ToList().FindIndex(d => d.Id == this.Input?.Id);
        this.settings.VoiceNoteInput = inputs[(i + 1) % inputs.Count].Id;
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<VoiceNote> Notes
    {
        get
        {
            if (!Directory.Exists(this.Folder))
                return [];

            return new DirectoryInfo(this.Folder)
                .EnumerateFiles("*.wav")
                .Where(f => f.FullName != this.recording)
                .Select(f => new VoiceNote(
                    f.FullName,
                    f.CreationTime,
                    TimeSpan.FromSeconds(Math.Max(0, f.Length - WavHeader) / (double)BytesPerSecond)
                ))
                .OrderByDescending(n => n.Recorded)
                .ToList();
        }
    }

    /// <summary>Starts a note, or stops (and keeps) the one being recorded.</summary>
    public async Task ToggleRecordingAsync()
    {
        if (this.recorder.IsRecording)
        {
            await this.StopRecordingAsync().ConfigureAwait(true);
            return;
        }

        var access = await this.recorder.RequestAccess().ConfigureAwait(true);
        this.IsDenied = access is AccessState.Denied or AccessState.Restricted;
        if (access != AccessState.Available)
        {
            this.Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        await this.StopPlayingAsync().ConfigureAwait(true);
        Directory.CreateDirectory(this.Folder);
        this.recording = Path.Combine(this.Folder, this.time.GetLocalNow().ToString(FileFormat) + ".wav");
        this.Level = 0;
        try
        {
            await this.recorder.StartAsync(new AudioRecordingOptions
            {
                Path = this.recording,
                InputDevice = this.Input,
                // Notes are for listening back, so the system's voice processing is worth having.
                Processing = new AudioProcessingOptions { NoiseSuppression = true, AutomaticGainControl = true }
            }).ConfigureAwait(true);
        }
        catch
        {
            this.recording = null;
        }
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    async Task StopRecordingAsync()
    {
        try
        {
            var result = await this.recorder.StopAsync().ConfigureAwait(true);
            // Nothing heard (stopped straight away): no empty note.
            if (result == null || result.Duration < TimeSpan.FromSeconds(0.5))
                TryDelete(result?.Path ?? this.recording);
        }
        finally
        {
            this.recording = null;
            this.Level = 0;
            this.Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Plays a note, or stops it if it is the one playing.</summary>
    public async Task TogglePlayAsync(VoiceNote note)
    {
        var wasPlaying = this.Playing == note.Path;
        await this.StopPlayingAsync().ConfigureAwait(true);
        if (wasPlaying)
            return;

        try
        {
            var playback = await this.player.StartAsync(note.Path).ConfigureAwait(true);
            this.playback = playback;
            this.Playing = note.Path;
            this.Changed?.Invoke(this, EventArgs.Empty);

            await playback.Completion.ConfigureAwait(true);
        }
        catch
        {
            // An unreadable note just doesn't play.
        }

        if (this.Playing == note.Path)
        {
            this.Playing = null;
            this.playback = null;
            this.Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task StopPlayingAsync()
    {
        if (this.playback is not { } playback)
            return;

        this.playback = null;
        this.Playing = null;
        await playback.StopAsync().ConfigureAwait(true);
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task DeleteAsync(VoiceNote note)
    {
        if (this.Playing == note.Path)
            await this.StopPlayingAsync().ConfigureAwait(true);
        TryDelete(note.Path);
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The note's name as the list shows it: when it was recorded.</summary>
    public string Describe(VoiceNote note)
    {
        var today = this.time.GetLocalNow().Date;
        var day = note.Recorded.Date == today ? "Today"
            : note.Recorded.Date == today.AddDays(-1) ? "Yesterday"
            : note.Recorded.ToString("ddd d MMM");
        return $"{day}, {note.Recorded:t}";
    }

    public static string Format(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    static void TryDelete(string? path)
    {
        try
        {
            if (path != null && File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Left behind is no worse than not recorded.
        }
    }
}
