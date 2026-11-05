using Microsoft.Extensions.Logging;
using Shiny;
using Shiny.Speech;

namespace ShinyNotch.Services;

/// <summary>
/// The teleprompter's state: whether it is running and how far it has scrolled. The notch reads the
/// offset on every frame and scrolls its text to it.
/// </summary>
/// <remarks>
/// Two ways to move: the timer scrolls at a set speed, or listening follows the reader's voice through the
/// script word by word (where the platform has speech recognition). Starting one stops the other.
/// </remarks>
[Singleton]
public sealed class TeleprompterService
{
    readonly AppSettings settings;
    readonly IDispatcherTimer timer;
    readonly TimeProvider time;
    readonly IDispatcher dispatcher;
    readonly ISpeechToTextService? speech;
    readonly ILogger logger;
    DateTimeOffset lastTick;

    public TeleprompterService(AppSettings settings, IDispatcher dispatcher, TimeProvider time, IEnumerable<ISpeechToTextService> speech, ILogger<TeleprompterService> logger)
    {
        this.settings = settings;
        this.time = time;
        this.dispatcher = dispatcher;
        this.logger = logger;
        this.timer = dispatcher.CreateTimer();
        this.timer.Interval = TimeSpan.FromMilliseconds(33);
        this.timer.Tick += (_, _) => this.OnTick();

        this.Follower = new ScriptFollower(this.Script);
        settings.Changed += (_, key) =>
        {
            // A new script starts the voice over from its first word. Subscribed before the notch's own
            // handler, so the tab it rebuilds already reads the new words.
            if (key == nameof(AppSettings.TeleprompterScript))
                this.Follower = new ScriptFollower(this.Script);
            // Turned off while listening: stop, and drop the highlight with it.
            else if (key == nameof(AppSettings.TeleprompterFollowVoice) && !this.CanFollowVoice)
            {
                this.Follower.Reset();
                this.ListenProblem = null;
                if (this.IsListening)
                    _ = this.StopListening();
            }
        };

        // Not every head registers speech recognition (Linux has no OS engine); then there's no listening.
        this.speech = speech.FirstOrDefault();
        if (this.speech != null)
        {
            this.speech.ResultReceived += (_, result) => this.dispatcher.Dispatch(() => this.OnHeard(result));
            this.speech.Error += (_, error) => this.dispatcher.Dispatch(() => this.OnSpeechError(error));
        }
    }

    public bool IsRunning { get; private set; }

    /// <summary>Following the reader's voice instead of scrolling on a timer.</summary>
    public bool IsListening { get; private set; }

    /// <summary>This platform can follow a voice. Never on Linux: the option isn't offered there at all.</summary>
    public bool CanListen => !OperatingSystem.IsLinux() && this.speech?.IsSupported == true;

    /// <summary>It can, and Settings says to offer it.</summary>
    public bool CanFollowVoice => this.CanListen && this.settings.TeleprompterFollowVoice;

    /// <summary>Why listening couldn't start (no permission, say), until it next does.</summary>
    public string? ListenProblem { get; private set; }

    /// <summary>The script as words, and the one the reader is on.</summary>
    public ScriptFollower Follower { get; private set; }

    /// <summary>Points scrolled from the top of the script.</summary>
    public double Offset { get; private set; }

    public string Script => this.settings.TeleprompterScript;
    public double FontSize => this.settings.TeleprompterFontSize;
    public double Speed => this.settings.TeleprompterSpeed;
    public double Width => this.settings.TeleprompterWidth;
    public double Height => this.settings.TeleprompterHeight;

    /// <summary>Raised ~30 times a second while running.</summary>
    public event EventHandler? Scrolled;

    /// <summary>Started, paused or restarted.</summary>
    public event EventHandler? StateChanged;

    /// <summary>The reader moved on to another word.</summary>
    public event EventHandler? WordChanged;

    public void Toggle()
    {
        if (this.IsListening)
            _ = this.StopListening();

        this.IsRunning = !this.IsRunning;
        if (this.IsRunning)
        {
            this.lastTick = this.time.GetUtcNow();
            this.timer.Start();
        }
        else
        {
            this.timer.Stop();
        }
        this.StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Restart()
    {
        this.Offset = 0;
        this.Follower.Reset();
        this.WordChanged?.Invoke(this, EventArgs.Empty);
        this.Scrolled?.Invoke(this, EventArgs.Empty);
        this.StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Nudge(double points)
    {
        this.Offset = Math.Max(0, this.Offset + points);
        this.Scrolled?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Scrolls to a point the tab worked out: where the word being read is.</summary>
    public void SeekTo(double offset)
    {
        this.Offset = Math.Max(0, offset);
        this.Scrolled?.Invoke(this, EventArgs.Empty);
    }

    public Task ToggleListening() => this.IsListening ? this.StopListening() : this.StartListening();

    async Task StartListening()
    {
        if (this.speech == null || this.speech.IsListening || !this.CanFollowVoice)
            return;

        try
        {
            var access = await this.speech.RequestAccess();
            if (access != AccessState.Available)
            {
                this.ListenProblem = access == AccessState.NotSupported
                    ? "Speech recognition isn't available here"
                    : "Allow the microphone and speech recognition in system settings";
                this.StateChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (this.IsRunning)
            {
                this.IsRunning = false;
                this.timer.Stop();
            }

            await this.speech.Start(new SpeechRecognitionOptions
            {
                // On the device where it can be: no network round trip, and no cap on a session's length.
                PreferOnDevice = true,
                // Reading pauses for breath, a slide, a sip of water; none of those should end an utterance early.
                SilenceTimeout = TimeSpan.FromSeconds(4)
            });
            this.IsListening = true;
            this.ListenProblem = null;
        }
        catch (Exception ex)
        {
            this.logger.LogWarning(ex, "Teleprompter could not start listening");
            this.ListenProblem = "Couldn't start listening";
        }
        this.StateChanged?.Invoke(this, EventArgs.Empty);
    }

    async Task StopListening()
    {
        this.IsListening = false;
        this.StateChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            if (this.speech != null)
                await this.speech.Stop();
        }
        catch (Exception ex)
        {
            this.logger.LogWarning(ex, "Teleprompter could not stop listening");
        }
    }

    void OnHeard(SpeechRecognitionResult result)
    {
        if (this.IsListening && this.Follower.Hear(result.Text, result.IsFinal))
            this.WordChanged?.Invoke(this, EventArgs.Empty);
    }

    void OnSpeechError(SpeechRecognitionError error)
    {
        this.logger.LogDebug(error.Exception, "Teleprompter speech: {Message}", error.Message);
        // The session gets over passing errors on its own; one it couldn't, it has stopped.
        if (this.IsListening && this.speech?.IsListening != true)
        {
            this.IsListening = false;
            this.ListenProblem = error.Message;
            this.StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ChangeSpeed(double delta) => this.settings.TeleprompterSpeed = this.Speed + delta;

    // The setting's Changed rebuilds the tab at the new size; raising StateChanged as well built it twice.
    public void ChangeFontSize(double delta) => this.settings.TeleprompterFontSize = this.FontSize + delta;

    void OnTick()
    {
        var now = this.time.GetUtcNow();
        this.Offset += this.Speed * (now - this.lastTick).TotalSeconds;
        this.lastTick = now;
        this.Scrolled?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The script ran off the end; stop where it is.</summary>
    public void ReachedEnd()
    {
        if (!this.IsRunning)
            return;

        this.IsRunning = false;
        this.timer.Stop();
        this.StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
