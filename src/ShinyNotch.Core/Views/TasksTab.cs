using ShinyNotch.Services;

namespace ShinyNotch.Views;

/// <summary>
/// The task list: a circle to check each one off (it's struck through, and cleared away a day later), a
/// cross to delete it, and a button that opens the quick entry popup to type new ones - the notch
/// itself never takes the keyboard.
/// </summary>
sealed class TasksTab : VerticalStackLayout
{
    /// <summary>Rows shown before the list scrolls, so a long list doesn't push the panel off the screen.</summary>
    const int RowsBeforeScrolling = 7;
    const double RowHeight = 30;

    public TasksTab(TasksService tasks, Action addTask)
    {
        this.Spacing = 8;
        var list = tasks.Tasks;

        var remaining = tasks.Remaining;
        this.Children.Add(new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            Children =
            {
                Ui.Caption(remaining switch
                {
                    0 when list.Count > 0 => "All done",
                    0 => "Tasks",
                    1 => "1 to do",
                    _ => $"{remaining} to do"
                }).CenteredVertically().At(column: 0),
                Ui.Chip("Add", addTask, selected: true, tint: Ui.Blue, symbol: "plus").At(column: 1)
            }
        });

        if (list.Count == 0)
        {
            this.Children.Add(Ui.Card(
                Ui.Label("Nothing to do. Add a task and check it off when it's done.", 12, color: Ui.Secondary).Centered(),
                new Thickness(12, 20)));
            return;
        }

        var rows = new VerticalStackLayout { Spacing = 0 };
        foreach (var task in list)
            rows.Children.Add(Row(tasks, task));

        View content = rows;
        if (list.Count > RowsBeforeScrolling)
            content = new ScrollView { Content = rows, HeightRequest = RowsBeforeScrolling * RowHeight };
        this.Children.Add(Ui.Card(content, new Thickness(6, 4)));

        if (list.Any(t => t.IsDone))
            this.Children.Add(Ui.Label("Checked-off tasks clear away after a day.", 10, color: Ui.Tertiary));
    }

    static View Row(TasksService tasks, TaskItem task)
    {
        var text = Ui.Label(task.Text, 13, color: task.IsDone ? Ui.Tertiary : Ui.Text);
        text.VerticalOptions = LayoutOptions.Center;
        text.LineBreakMode = LineBreakMode.TailTruncation;
        text.MaxLines = 1;
        if (task.IsDone)
            text.TextDecorations = TextDecorations.Strikethrough;

        return new Grid
        {
            HeightRequest = RowHeight,
            ColumnSpacing = 4,
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            Children =
            {
                Ui.SymbolButton(
                    task.IsDone ? "checkmark.circle.fill" : "circle",
                    () => tasks.Toggle(task.Id),
                    14,
                    task.IsDone ? Ui.Green : Ui.Secondary,
                    task.IsDone ? "Not done" : "Done",
                    padding: new Thickness(4)
                ).At(column: 0),
                text.At(column: 1),
                Ui.SymbolButton("xmark", () => tasks.Delete(task.Id), 9, Ui.Tertiary, "Delete", padding: new Thickness(5)).At(column: 2)
            }
        };
    }
}
