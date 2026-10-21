using System.Text.Json;
using System.Text.Json.Serialization;
using Shiny;

namespace ShinyNotch.Services;

/// <param name="Completed">When it was checked off (UTC), or null while it's still to do.</param>
public sealed record TaskItem(string Id, string Text, DateTimeOffset Created, DateTimeOffset? Completed)
{
    public bool IsDone => this.Completed != null;
}

[JsonSerializable(typeof(List<TaskItem>))]
sealed partial class TasksJson : JsonSerializerContext;

/// <summary>
/// A to-do list. Checking a task off keeps it - struck through - for a day, so a slip of the pointer can
/// be undone and the day's work stays in sight; after that it is deleted on its own.
/// </summary>
[Singleton]
public sealed class TasksService
{
    /// <summary>How long a checked-off task stays before it's cleared away.</summary>
    public static readonly TimeSpan KeepCompleted = TimeSpan.FromDays(1);

    readonly AppSettings settings;
    readonly TimeProvider time;
    readonly List<TaskItem> tasks;

    public TasksService(AppSettings settings, TimeProvider time)
    {
        this.settings = settings;
        this.time = time;
        this.tasks = Load(settings.Tasks);
        this.Prune();
    }

    /// <summary>A task was added, checked, unchecked, deleted or cleared away.</summary>
    public event EventHandler? Changed;

    /// <summary>What's still to do in the order it was added, then what's done, the latest first.</summary>
    public IReadOnlyList<TaskItem> Tasks => this.tasks
        .Where(t => !t.IsDone)
        .Concat(this.tasks.Where(t => t.IsDone).OrderByDescending(t => t.Completed))
        .ToList();

    public int Remaining => this.tasks.Count(t => !t.IsDone);

    public void Add(string text)
    {
        text = text.Trim();
        if (text.Length == 0)
            return;

        this.tasks.Add(new TaskItem(Guid.NewGuid().ToString("N"), text, this.time.GetUtcNow(), null));
        this.Save();
    }

    /// <summary>Checks a task off, or puts a checked one back on the list.</summary>
    public void Toggle(string id)
    {
        var i = this.tasks.FindIndex(t => t.Id == id);
        if (i < 0)
            return;

        var task = this.tasks[i];
        this.tasks[i] = task with { Completed = task.IsDone ? null : this.time.GetUtcNow() };
        this.Save();
    }

    public void Delete(string id)
    {
        if (this.tasks.RemoveAll(t => t.Id == id) > 0)
            this.Save();
    }

    /// <summary>Deletes tasks checked off more than <see cref="KeepCompleted"/> ago. Cheap; called on the notch's clock.</summary>
    public void Prune()
    {
        var cutoff = this.time.GetUtcNow() - KeepCompleted;
        if (this.tasks.RemoveAll(t => t.Completed < cutoff) > 0)
            this.Save();
    }

    void Save()
    {
        this.settings.Tasks = JsonSerializer.Serialize(this.tasks, TasksJson.Default.ListTaskItem);
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    static List<TaskItem> Load(string? json)
    {
        if (String.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize(json, TasksJson.Default.ListTaskItem) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
