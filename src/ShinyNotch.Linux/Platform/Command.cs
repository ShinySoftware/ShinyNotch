using System.Diagnostics;

namespace ShinyNotch.Linux;

/// <summary>Runs the small desktop tools the head leans on (pactl, xdg-open, canberra-gtk-play).</summary>
static class Command
{
    /// <summary>Runs a tool to the end and returns what it printed, or null when it's missing or failed.</summary>
    public static string? Read(string file, params string[] args)
    {
        try
        {
            using var process = Process.Start(Info(file, true, args));
            if (process == null)
                return null;

            var output = process.StandardOutput.ReadToEnd();
            // A hung tool would hang the main thread; none of these take more than a moment.
            if (!process.WaitForExit(2000))
            {
                process.Kill();
                return null;
            }
            return process.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Starts a tool and leaves it to finish on its own. False when it couldn't be started.</summary>
    public static bool Start(string file, params string[] args)
    {
        try
        {
            // Not redirected: nobody reads it, and a full pipe would stall the tool.
            using var process = Process.Start(Info(file, false, args));
            return process != null;
        }
        catch
        {
            return false;
        }
    }

    public static ProcessStartInfo Info(string file, bool redirect, params string[] args)
    {
        var info = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = redirect,
            UseShellExecute = false
        };
        // Untranslated, so "Mute: yes" reads the same in every locale.
        info.Environment["LC_ALL"] = "C";
        foreach (var arg in args)
            info.ArgumentList.Add(arg);
        return info;
    }
}
