using Microsoft.Maui.Controls.Shapes;
using ShinyNotch.Services;

namespace ShinyNotch.Views;

/// <summary>
/// A pocket calculator: the display and the tape of recent sums on the left, the keypad on the right. The
/// notch window never takes the keyboard, so it's clicked rather than typed. Keys redraw the display in
/// place instead of rebuilding the panel; picking a sum on the tape brings its answer back.
/// </summary>
sealed class CalculatorTab : Grid
{
    const double KeyWidth = 54;
    const double KeyHeight = 34;
    const double KeySpacing = 6;
    const double KeypadHeight = 5 * KeyHeight + 4 * KeySpacing;
    const double DisplayHeight = 80;
    const double TapeRowHeight = 24;

    static readonly string[][] Keys =
    [
        ["AC", "±", "%", "÷"],
        ["7", "8", "9", "×"],
        ["4", "5", "6", "−"],
        ["1", "2", "3", "+"],
        ["⌫", "0", ".", "="]
    ];

    readonly CalculatorService calculator;
    readonly Label expression;
    readonly Label display;
    readonly Label clear;
    readonly Dictionary<char, Border> operators = new();
    readonly VerticalStackLayout? tapeRows;

    public CalculatorTab(CalculatorService calculator, AppSettings settings)
    {
        this.calculator = calculator;
        this.ColumnSpacing = 14;
        this.ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)];

        // The display: the sum so far above the number, and a copy button.
        this.expression = Ui.Label("", 12, color: Ui.Secondary);
        this.expression.HorizontalTextAlignment = TextAlignment.End;
        this.expression.LineBreakMode = LineBreakMode.HeadTruncation;
        this.display = Ui.Label("", 30, bold: true);
        this.display.HorizontalTextAlignment = TextAlignment.End;
        this.display.LineBreakMode = LineBreakMode.HeadTruncation;
        var copy = Ui.SymbolButton("doc.on.doc", calculator.Copy, 12, Ui.Tertiary, "Copy", padding: new Thickness(4));
        copy.HorizontalOptions = LayoutOptions.Start;
        copy.VerticalOptions = LayoutOptions.Start;
        var screen = Ui.Card(new Grid
        {
            RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star)],
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)],
            ColumnSpacing = 6,
            Children =
            {
                copy.At(row: 0, column: 0),
                this.expression.At(row: 0, column: 1),
                this.display.At(row: 1, columnSpan: 2)
            }
        }, new Thickness(10, 8, 12, 8));
        screen.HeightRequest = settings.CalculatorShowTape ? DisplayHeight : KeypadHeight;

        var left = new VerticalStackLayout { Spacing = 8, Children = { screen } };
        if (settings.CalculatorShowTape)
        {
            this.tapeRows = new VerticalStackLayout { Spacing = 0 };
            var tape = new ScrollView { Content = this.tapeRows, HeightRequest = KeypadHeight - DisplayHeight - 8 - 2 * 4 };
            left.Children.Add(Ui.Card(tape, new Thickness(6, 4)));
        }
        this.Children.Add(left.At(column: 0));

        var keypad = new Grid { RowSpacing = KeySpacing, ColumnSpacing = KeySpacing, VerticalOptions = LayoutOptions.Start };
        for (var r = 0; r < Keys.Length; r++)
        {
            keypad.RowDefinitions.Add(new RowDefinition(KeyHeight));
            for (var c = 0; c < Keys[r].Length; c++)
            {
                if (r == 0)
                    keypad.ColumnDefinitions.Add(new ColumnDefinition(KeyWidth));
                keypad.Children.Add(this.Key(Keys[r][c], out var label).At(row: r, column: c));
                if (Keys[r][c] == "AC")
                    this.clear = label;
            }
        }
        this.clear ??= new Label();
        this.Children.Add(keypad.At(column: 1));

        this.Refresh();
        this.RefreshTape();
    }

    /// <summary>A key: operators in orange, the functions along the top lighter, digits darker.</summary>
    Border Key(string text, out Label label)
    {
        var op = text is "÷" or "×" or "−" or "+" or "=";
        var function = text is "AC" or "±" or "%" or "⌫";
        label = Ui.Label(text, op ? 18 : 15, bold: op || function, color: op ? Colors.White : Ui.Text).Centered();
        label.VerticalOptions = LayoutOptions.Center;

        var key = new Border
        {
            AutomationId = text,
            StrokeThickness = 0,
            Stroke = Colors.White,
            StrokeShape = new RoundRectangle { CornerRadius = 9 },
            BackgroundColor = op ? Ui.Orange : function ? Ui.Separator : Ui.Raised,
            Content = label
        }.Pressable(() =>
        {
            this.Press(text);
            this.Refresh();
        });
        SemanticProperties.SetDescription(key, text switch
        {
            "⌫" => "Delete",
            "±" => "Change sign",
            "AC" => "Clear",
            _ => text
        });
        if (op && text != "=")
            this.operators[text[0]] = key;
        return key;
    }

    void Press(string key)
    {
        var c = this.calculator;
        switch (key)
        {
            case "AC": c.Clear(); break;
            case "±": c.Negate(); break;
            case "%": c.Percent(); break;
            case "⌫": c.Backspace(); break;
            case ".": c.Point(); break;
            case "=":
                c.Equal();
                this.RefreshTape();
                break;
            case [var digit] when Char.IsDigit(digit): c.Digit(digit); break;
            default: c.Operator(key[0]); break;
        }
    }

    /// <summary>The display, the clear key's name and the waiting operator's ring.</summary>
    void Refresh()
    {
        var c = this.calculator;
        this.expression.Text = c.Expression;
        var shown = c.Display;
        this.display.Text = shown;
        // Smaller for long numbers, rather than losing their first digits.
        this.display.FontSize = shown.Length switch { > 16 => 20, > 12 => 24, _ => 30 };
        this.clear.Text = c.ClearsAll ? "AC" : "C";
        foreach (var (op, key) in this.operators)
            key.StrokeThickness = c.PendingOperator == op ? 2 : 0;
    }

    /// <summary>The tape, latest first. Picking a sum brings its answer back to the display.</summary>
    void RefreshTape()
    {
        if (this.tapeRows == null)
            return;

        this.tapeRows.Children.Clear();
        var tape = this.calculator.Tape;
        var header = new Grid
        {
            HeightRequest = TapeRowHeight,
            Padding = new Thickness(6, 0, 0, 0),
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            Children = { Ui.Caption("History").CenteredVertically().At(column: 0) }
        };
        if (tape.Count > 0)
            header.Children.Add(Ui.SymbolButton("trash", this.calculator.ClearTape, 10, Ui.Tertiary, "Clear history", padding: new Thickness(5)).At(column: 1));
        this.tapeRows.Children.Add(header);

        if (tape.Count == 0)
        {
            this.tapeRows.Children.Add(Ui.Label("Sums show up here once you press =.", 11, color: Ui.Tertiary).Centered());
            return;
        }

        foreach (var item in tape)
        {
            var sum = Ui.Label(item.Expression, 11, color: Ui.Tertiary);
            sum.LineBreakMode = LineBreakMode.HeadTruncation;
            var answer = Ui.Label("= " + this.calculator.Format(item.Result), 12, bold: true, color: Ui.Secondary);
            answer.HorizontalTextAlignment = TextAlignment.End;
            var row = new Border
            {
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 7 },
                BackgroundColor = Colors.Transparent,
                HeightRequest = TapeRowHeight,
                Padding = new Thickness(6, 0),
                Content = new Grid
                {
                    ColumnSpacing = 8,
                    ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
                    Children = { sum.At(column: 0), answer.At(column: 1) }
                }
            }.Pressable(() =>
            {
                this.calculator.Use(item);
                this.Refresh();
            });
            SemanticProperties.SetDescription(row, $"Use {this.calculator.Format(item.Result)}");
            this.tapeRows.Children.Add(row);
        }
    }
}
