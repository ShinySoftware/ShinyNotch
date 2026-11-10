using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shiny;
using ShinyNotch.Platform;

namespace ShinyNotch.Services;

/// <summary>A finished sum on the tape: what was typed and what it came to.</summary>
public sealed record CalculatorEntry(string Expression, decimal Result);

[JsonSerializable(typeof(List<CalculatorEntry>))]
sealed partial class CalculatorJson : JsonSerializerContext;

/// <summary>
/// A four-function calculator with the usual pocket-calculator keys. × and ÷ go before + and −, as on the
/// system calculators, so 2 + 3 × 4 is 14. The sum in progress lives here rather than in the tab, so it
/// survives the panel closing and being rebuilt.
/// </summary>
/// <remarks>
/// Works in <see cref="decimal"/>, so 0.1 + 0.2 is 0.3. Answers are rounded to <see cref="MaxDecimals"/>
/// places; anything too big for a decimal, or a division by zero, shows "Error" until the next key.
/// </remarks>
[Singleton]
public sealed class CalculatorService
{
    /// <summary>Sums kept on the tape.</summary>
    public const int TapeLimit = 20;
    const int MaxDigits = 15;
    const int MaxDecimals = 10;

    readonly AppSettings settings;
    readonly IClipboardWatcher clipboard;
    readonly List<CalculatorEntry> tape;
    // Numbers and operators, alternating, for the sum so far; the number being typed isn't in it yet.
    readonly List<object> terms = [];
    // What's being typed, in invariant digits ("12.5"), or null when nothing is.
    string? entry;
    // The last answer, shown until something is typed.
    decimal value;
    bool error;

    public CalculatorService(AppSettings settings, IClipboardWatcher clipboard)
    {
        this.settings = settings;
        this.clipboard = clipboard;
        this.tape = Load(settings.CalculatorTape);
    }

    /// <summary>The tape was cleared. Keys don't raise this: the tab redraws itself as they're pressed.</summary>
    public event EventHandler? Changed;

    /// <summary>Finished sums, latest first.</summary>
    public IReadOnlyList<CalculatorEntry> Tape => this.tape;

    /// <summary>The big number: what's being typed, else the last answer.</summary>
    public string Display => this.error ? "Error" : this.entry != null ? this.FormatEntry(this.entry) : this.Format(this.value);

    /// <summary>The sum so far, above the big number ("12 + 3 ×"). Empty when there's none.</summary>
    public string Expression => String.Join(" ", this.terms.Select(t => t is decimal d ? this.Format(d) : t.ToString()));

    /// <summary>The operator waiting for its second number, to light its key.</summary>
    public char? PendingOperator => this.entry == null && this.terms.LastOrDefault() is char op ? op : null;

    /// <summary>Clear takes the number being typed; with nothing typed, All Clear takes the whole sum.</summary>
    public bool ClearsAll => this.entry == null || this.error;

    public void Digit(char digit)
    {
        this.Recover();
        if (this.entry == null || this.entry == "0")
            this.entry = digit.ToString();
        else if (this.entry.Count(Char.IsDigit) < MaxDigits)
            this.entry += digit;
    }

    public void Point()
    {
        this.Recover();
        if (this.entry == null)
            this.entry = "0.";
        else if (!this.entry.Contains('.') && this.entry.Count(Char.IsDigit) < MaxDigits)
            this.entry += ".";
    }

    public void Backspace()
    {
        if (this.error || this.entry == null)
            return;
        this.entry = this.entry[..^1];
        if (this.entry is "" or "-")
            this.entry = "0";
    }

    public void Clear()
    {
        if (!this.ClearsAll)
        {
            this.entry = null;
            this.value = 0;
            return;
        }
        this.terms.Clear();
        this.entry = null;
        this.value = 0;
        this.error = false;
    }

    public void Negate()
    {
        if (this.error)
            return;
        if (this.entry != null)
            this.entry = this.entry.StartsWith('-') ? this.entry[1..] : "-" + this.entry;
        else
            this.value = -this.value;
    }

    /// <summary>
    /// A hundredth of the number - or, after + or −, that percent of the number before it, so
    /// 80 + 10% is 88.
    /// </summary>
    public void Percent()
    {
        if (this.error)
            return;
        var n = this.Current / 100;
        if (this.terms.Count >= 2 && this.terms[^1] is '+' or '−' && this.terms[^2] is decimal before)
            n *= before;
        this.entry = Invariant(Math.Round(n, MaxDecimals));
    }

    /// <param name="op">One of + − × ÷.</param>
    public void Operator(char op)
    {
        if (this.error)
            return;
        if (this.entry != null)
            this.terms.Add(Parse(this.entry));
        else if (this.terms.Count > 0 && this.terms[^1] is char)
            this.terms.RemoveAt(this.terms.Count - 1);
        else if (this.terms.Count == 0)
            // Carry on from the last answer.
            this.terms.Add(this.value);
        this.terms.Add(op);
        this.entry = null;
    }

    /// <summary>Works the sum out, shows the answer and puts the sum on the tape.</summary>
    public void Equal()
    {
        if (this.error || this.terms.Count == 0)
            return;

        // "12 + =" uses the 12 again, as calculators do.
        var number = this.entry != null ? Parse(this.entry) : this.terms[^1] is char && this.terms.Count >= 2 ? (decimal)this.terms[^2] : this.value;
        this.terms.Add(number);
        var expression = this.Expression;

        try
        {
            this.value = Math.Round(Evaluate(this.terms), MaxDecimals);
        }
        catch (Exception ex) when (ex is DivideByZeroException or OverflowException)
        {
            this.terms.Clear();
            this.entry = null;
            this.error = true;
            return;
        }

        this.terms.Clear();
        this.entry = null;
        this.tape.Insert(0, new CalculatorEntry(expression, this.value));
        if (this.tape.Count > TapeLimit)
            this.tape.RemoveRange(TapeLimit, this.tape.Count - TapeLimit);
        this.Save();

        if (this.settings.CalculatorCopyOnEquals)
            this.clipboard.SetText(Invariant(this.value));
    }

    /// <summary>Puts a past answer back, as if it had just been typed.</summary>
    public void Use(CalculatorEntry item)
    {
        this.Recover();
        this.entry = Invariant(item.Result);
    }

    /// <summary>Copies the big number, without thousands separators so it pastes as a number anywhere.</summary>
    public void Copy()
    {
        if (!this.error)
            this.clipboard.SetText(Invariant(this.Current));
    }

    public void ClearTape()
    {
        this.tape.Clear();
        this.Save();
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The number on display, as a number.</summary>
    decimal Current => this.entry != null ? Parse(this.entry) : this.value;

    /// <summary>A number key after an error starts afresh.</summary>
    void Recover()
    {
        if (this.error)
            this.Clear();
        this.error = false;
    }

    /// <summary>× and ÷ first, left to right, then + and −.</summary>
    static decimal Evaluate(List<object> terms)
    {
        var sums = new List<object> { (decimal)terms[0] };
        for (var i = 1; i + 1 < terms.Count; i += 2)
        {
            var op = (char)terms[i];
            var n = (decimal)terms[i + 1];
            if (op is '×' or '÷')
            {
                var left = (decimal)sums[^1];
                sums[^1] = op == '×' ? left * n : left / n;
            }
            else
            {
                sums.Add(op);
                sums.Add(n);
            }
        }

        var total = (decimal)sums[0];
        for (var i = 1; i + 1 < sums.Count; i += 2)
            total = (char)sums[i] == '+' ? total + (decimal)sums[i + 1] : total - (decimal)sums[i + 1];
        return total;
    }

    /// <summary>An answer for showing: the region's separators, and thousands grouped if Settings says so.</summary>
    public string Format(decimal n)
        => n.ToString(this.settings.CalculatorGroupDigits ? "#,##0.##########" : "0.##########", CultureInfo.CurrentCulture);

    /// <summary>
    /// What's being typed, grouped like an answer but keeping what an answer would drop: a trailing point,
    /// trailing zeros ("2.50") and a minus on zero.
    /// </summary>
    string FormatEntry(string text)
    {
        var format = CultureInfo.CurrentCulture.NumberFormat;
        var negative = text.StartsWith('-');
        var digits = negative ? text[1..] : text;
        var point = digits.IndexOf('.');
        var whole = point < 0 ? digits : digits[..point];
        var shown = this.settings.CalculatorGroupDigits ? Decimal.Parse(whole, CultureInfo.InvariantCulture).ToString("#,##0", CultureInfo.CurrentCulture) : whole;
        if (point >= 0)
            shown += format.NumberDecimalSeparator + digits[(point + 1)..];
        return negative ? format.NegativeSign + shown : shown;
    }

    static decimal Parse(string text) => Decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture);

    static string Invariant(decimal n) => n.ToString("0.##########", CultureInfo.InvariantCulture);

    void Save() => this.settings.CalculatorTape = JsonSerializer.Serialize(this.tape, CalculatorJson.Default.ListCalculatorEntry);

    /// <summary>The saved tape, or an empty one if there's none or it can't be read.</summary>
    static List<CalculatorEntry> Load(string? json)
    {
        if (String.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return JsonSerializer.Deserialize(json, CalculatorJson.Default.ListCalculatorEntry) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
