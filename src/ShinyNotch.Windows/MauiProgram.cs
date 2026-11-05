using Shiny;
using ShinyNotch.Windows;

namespace ShinyNotch;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseShinyNotch()
            .UseNotchMirror();

        builder.Services.AddCalendarStore();
        builder.Services.AddAudioServices();
        // The teleprompter follows your voice, when that's turned on in Settings.
        builder.Services.AddSpeechToText();
        // Everything marked [Singleton] in this head: the Platform implementations.
        builder.Services.AddPlatformServices();
        // No ICaptureTab: macshot is macOS only, so Windows has no Capture tab.

        return builder.Build();
    }
}
