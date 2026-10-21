using Shiny;
using Shiny.Maui.Controls.QuickEntry;
using ShinyNotch.Platform;
using ShinyNotch.Views;

namespace ShinyNotch.Services;

/// <summary>
/// Typing a new task. The notch window never takes the keyboard, so tasks are typed into Shiny's quick
/// entry popup: a window of its own, over whatever app is in front. Return adds the task and clears the
/// box for the next one; Escape or clicking away closes it.
/// </summary>
[Singleton]
public sealed class TaskEntry
{
    const string WindowTitle = "New Task";

    readonly IQuickEntryService quickEntry;
    readonly TasksService tasks;

    public TaskEntry(IQuickEntryService quickEntry, TasksService tasks, IPlatformShell shell)
    {
        this.quickEntry = quickEntry;
        this.tasks = tasks;
        // The popup is a floating window and the notch a status-bar one, so it opened under the panel.
        quickEntry.Opened += (_, _) => shell.RaiseAboveNotch(WindowTitle);

        var options = quickEntry.Options;
        options.Width = 520;
        options.WindowTitle = WindowTitle;
        options.DismissOnFocusLost = true;
        options.DismissOnEscape = true;
        options.ContentFactory = () =>
        {
            var prompt = new PromptView
            {
                Placeholder = "Add a task, then Return. Esc when you're done.",
                ShowMicrophone = false,
                ClearOnSubmit = true,
                AccentColor = Ui.Blue,
                SurfaceColor = Ui.Surface,
                TextColor = Ui.Text,
                PlaceholderColor = Ui.Tertiary
            };
            prompt.Submitted += (_, e) => this.tasks.Add(e.Text);
            return prompt;
        };
    }

    public bool IsOpen => this.quickEntry.IsOpen;

    /// <summary>The popup closed, whichever way.</summary>
    public event EventHandler? Closed
    {
        add => this.quickEntry.Closed += value;
        remove => this.quickEntry.Closed -= value;
    }

    public void Show() => this.quickEntry.Show();
}
