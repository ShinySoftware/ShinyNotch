using ShinyNotch.Platform;

namespace ShinyNotch.Services;

/// <summary>
/// A shelf for files: drop them on the notch, then copy them back out to paste anywhere, share them,
/// or reveal them. Holds references, not copies, and forgets files that have since been moved or deleted.
/// </summary>
public sealed class FileTrayService
{
    readonly AppSettings settings;
    readonly List<string> files;

    public FileTrayService(AppSettings settings, IFileActions actions)
    {
        this.settings = settings;
        this.Actions = actions;
        this.files = settings.TrayFiles.Where(Exists).ToList();
    }

    public IReadOnlyList<string> Files => this.files;

    public event EventHandler? Changed;

    public void Add(IEnumerable<string> paths)
    {
        var added = false;
        foreach (var path in paths)
        {
            if (Exists(path) && !this.files.Contains(path))
            {
                this.files.Add(path);
                added = true;
            }
        }
        if (added)
            this.Save();
    }

    public void Remove(string path)
    {
        if (this.files.Remove(path))
            this.Save();
    }

    public void Clear()
    {
        this.files.Clear();
        this.Save();
    }

    /// <summary>Drops entries whose file has gone. Called before the tray is shown.</summary>
    public void Prune()
    {
        if (this.files.RemoveAll(p => !Exists(p)) > 0)
            this.Save();
    }

    public IFileActions Actions { get; }

    static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    void Save()
    {
        this.settings.TrayFiles = this.files.ToList();
        this.Changed?.Invoke(this, EventArgs.Empty);
    }
}
