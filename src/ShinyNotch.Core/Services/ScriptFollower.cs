using System.Text;
using System.Text.RegularExpressions;

namespace ShinyNotch.Services;

/// <summary>One word of the script: where it sits in its paragraph, and the form speech is matched against.</summary>
/// <param name="Paragraph">The script's line it is on (lines are split on newlines).</param>
/// <param name="Start">Its first character within that line.</param>
/// <param name="Length">Its length there, punctuation included.</param>
/// <param name="Key">Lower case, letters and digits only: "Don't," is "dont".</param>
public sealed record ScriptWord(int Paragraph, int Start, int Length, string Key);

/// <summary>
/// Follows the reader through the script from what speech recognition hears, word by word.
/// </summary>
/// <remarks>
/// Recognizers send an utterance's text again and again as it grows (and revise it as they go), then once
/// more as final; the next utterance starts afresh. So each result is aligned again from where its utterance
/// began, which makes revisions harmless, and a final result moves that starting point on.
///
/// Alignment walks the heard words, matching each a few words ahead of the last match, so a skipped or
/// misheard word doesn't lose the place. Short words ("a", "the") only match right where expected — they're
/// everywhere, and would drag the place forward. When the latest few heard words match nothing nearby the
/// reader has jumped (skipped a paragraph, gone back to repeat one), and the last three are looked for
/// elsewhere in the script.
/// </remarks>
public sealed partial class ScriptFollower
{
    /// <summary>How many words ahead of the expected one a heard word may match.</summary>
    const int Lookahead = 5;
    const int ShortWord = 3;
    const int JumpPhrase = 3;

    readonly string[] keys;
    /// <summary>The next word expected when the current utterance began.</summary>
    int anchor;

    public ScriptFollower(string script)
    {
        this.Paragraphs = script.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var words = new List<ScriptWord>();
        for (var p = 0; p < this.Paragraphs.Count; p++)
        {
            foreach (Match m in WordPattern().Matches(this.Paragraphs[p]))
            {
                var key = Normalize(m.Value);
                if (key.Length > 0)
                    words.Add(new ScriptWord(p, m.Index, m.Length, key));
            }
        }
        this.Words = words;
        this.keys = words.Select(w => w.Key).ToArray();
    }

    public IReadOnlyList<string> Paragraphs { get; }

    public IReadOnlyList<ScriptWord> Words { get; }

    /// <summary>The word last read, or -1 before the first.</summary>
    public int Current { get; private set; } = -1;

    public void Reset()
    {
        this.Current = -1;
        this.anchor = 0;
    }

    /// <summary>Takes the place on from a recognition result. True when the current word moved.</summary>
    public bool Hear(string text, bool isFinal)
    {
        var heard = Split(text);
        var before = this.Current;

        var next = this.anchor;
        var current = this.anchor - 1;
        var misses = 0;
        foreach (var word in heard)
        {
            var found = this.Find(word, next);
            if (found >= 0)
            {
                current = found;
                next = found + 1;
                misses = 0;
            }
            else
            {
                misses++;
            }
        }

        // Lost: the reader is somewhere else in the script. Find the phrase just said.
        if (misses >= JumpPhrase && heard.Length >= JumpPhrase)
        {
            var jumped = this.FindPhrase(heard[^JumpPhrase..], next);
            if (jumped >= 0)
            {
                current = jumped;
                next = jumped + 1;
            }
        }

        // With nothing matched yet this utterance, that is where the last one left off.
        this.Current = current;
        if (isFinal)
            this.anchor = next;

        return this.Current != before;
    }

    int Find(string word, int from)
    {
        var reach = word.Length <= ShortWord ? 1 : Lookahead + 1;
        for (var i = from; i < Math.Min(from + reach, this.keys.Length); i++)
        {
            if (this.keys[i] == word)
                return i;
        }
        return -1;
    }

    /// <summary>The last word of the phrase's occurrence nearest <paramref name="near"/>, forward first.</summary>
    int FindPhrase(string[] phrase, int near)
    {
        var best = -1;
        var bestDistance = int.MaxValue;
        for (var i = 0; i + phrase.Length <= this.keys.Length; i++)
        {
            var match = true;
            for (var j = 0; j < phrase.Length && match; j++)
                match = this.keys[i + j] == phrase[j];
            if (!match)
                continue;

            var end = i + phrase.Length - 1;
            // Going back costs double: re-reading happens, but less often than skipping on.
            var distance = end >= near ? end - near : (near - end) * 2;
            if (distance < bestDistance)
            {
                best = end;
                bestDistance = distance;
            }
        }
        return best;
    }

    static string[] Split(string text) => WordPattern()
        .Matches(text)
        .Select(m => Normalize(m.Value))
        .Where(k => k.Length > 0)
        .ToArray();

    static string Normalize(string word)
    {
        var sb = new StringBuilder(word.Length);
        foreach (var c in word.Normalize(NormalizationForm.FormD))
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    [GeneratedRegex(@"\S+")]
    private static partial Regex WordPattern();
}
