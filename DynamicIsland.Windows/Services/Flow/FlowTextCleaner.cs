using System.Text;
using System.Text.RegularExpressions;

namespace DynamicIsland.Windows.Services.Flow;

/// <summary>
/// Local, deterministic cleanup for dictated text. No network, no model: it runs on
/// every dictation (Basic mode) and is the fallback whenever AI cleanup fails or
/// looks like it rewrote too much.
/// </summary>
public static partial class FlowTextCleaner
{
    /// <summary>Removes Whisper's non-speech tags like [BLANK_AUDIO], (music), * coughs *.</summary>
    public static string StripWhisperArtifacts(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var t = BracketTag().Replace(text, " ");
        t = StarTag().Replace(t, " ");
        // Whisper sometimes starts a new segment with no leading space ("farm zone.Parmesan").
        t = GluedSentence().Replace(t, " ");
        t = MultiSpace().Replace(t, " ").Trim();
        return t;
    }

    private static readonly string[] SilencePhrases = ["thank you", "thanks for watching", "you", "bye"];

    /// <summary>
    /// Whisper sometimes "hears" a stock phrase in near-silence. Only treat the text as that kind of
    /// guess when it is one of those phrases and the audio had almost no speech in it, so someone who
    /// really says "Thank you" still gets it pasted.
    /// </summary>
    public static bool IsLikelyHallucination(string text, double voicedSeconds)
    {
        var t = text.Trim().TrimEnd('.', '!', ' ').ToLowerInvariant();
        return voicedSeconds < 0.25 && SilencePhrases.Contains(t);
    }

    /// <summary>Basic cleanup: fillers, stutters, spoken punctuation, "scratch that", capitals.</summary>
    public static string Basic(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var t = " " + raw.Trim() + " ";

        // Spoken commands first, while the words are still intact.
        t = NewParagraph().Replace(t, " \n\n ");
        t = NewLine().Replace(t, " \n ");
        t = ScratchThat().Replace(t, " ");
        t = SpokenMark(t, "question mark", "?");
        t = SpokenMark(t, "exclamation point", "!");
        t = SpokenMark(t, "exclamation mark", "!");
        t = SpokenMark(t, "full stop", ".");
        t = SpokenMark(t, "period", ".", requireEnd: true);
        t = SpokenMark(t, "comma", ",");
        t = SpokenMark(t, "colon", ":");

        // Fillers. "like" and "you know" carry meaning too often to remove safely here.
        t = Fillers().Replace(t, " ");
        // Stutters: "the the team" -> "the team" (not "bye bye", which is short and deliberate).
        t = Stutter().Replace(t, "$1");

        // Tidy spacing around punctuation and newlines.
        t = SpaceBeforePunct().Replace(t, "$1");
        t = MultiSpace().Replace(t, " ");
        t = SpaceAroundNewline().Replace(t, "\n");
        t = RepeatedPunct().Replace(t, "$1");
        t = LeadingPunct().Replace(t, "");
        t = t.Trim(' ', ',');

        t = Capitalize(t);
        t = StandaloneI().Replace(t, "I");
        if (t.Length > 0 && char.IsLetterOrDigit(t[^1]) && WordCount(t) >= 4) t += ".";
        return t;
    }

    public static int WordCount(string text) =>
        string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>
    /// Guards AI cleanup: if the rewrite is far shorter or longer than the speech, or
    /// looks like the model answered the dictation, keep the local version instead.
    /// </summary>
    public static bool LooksSafe(string basic, string ai)
    {
        if (string.IsNullOrWhiteSpace(ai)) return false;
        var a = WordCount(basic);
        var b = WordCount(ai);
        if (a == 0) return false;
        var ratio = b / (double)a;
        if (a >= 6 && ratio > 1.6) return false;
        if (a >= 6 && ratio < 0.45)
        {
            // A big cut is only plausible when the speaker corrected themselves ("tomato soup... actually
            // no, chicken parmesan"), and then the result must be built from words they actually said.
            if (ratio < 0.15 || !CorrectionCue().IsMatch(basic) || NovelShare(basic, ai) > 0.2) return false;
        }
        if (a < 6 && b > a + 6) return false;
        var lower = ai.TrimStart().ToLowerInvariant();
        string[] tells = ["sure", "here is", "here's", "certainly", "i can't", "i cannot", "as an ai", "cleaned text", "```"];
        return !tells.Any(t => lower.StartsWith(t) && !basic.TrimStart().ToLowerInvariant().StartsWith(t));
    }

    /// <summary>Share of the output's words (ignoring numbers) that the speaker never said.</summary>
    internal static double NovelShare(string said, string output)
    {
        static IEnumerable<string> Words(string text) =>
            Word().Matches(text.ToLowerInvariant()).Select(m => m.Value).Where(w => !w.All(char.IsDigit));
        var heard = Words(said).ToHashSet();
        var produced = Words(output).ToList();
        if (produced.Count == 0) return 1;
        return produced.Count(w => !heard.Contains(w)) / (double)produced.Count;
    }

    private static string SpokenMark(string text, string words, string mark, bool requireEnd = false)
    {
        // "period" is only punctuation at a phrase boundary ("the period of time" stays).
        var pattern = requireEnd
            ? $@"\s+{Regex.Escape(words)}(?=\s*(\n|$|(?-i:[A-Z])|(and|but|so|then|i|we|it)\b)|\s*$)"
            : $@"\s+{Regex.Escape(words)}\b";
        return Regex.Replace(text, pattern, mark + " ", RegexOptions.IgnoreCase);
    }

    private static string Capitalize(string text) =>
        SentenceStart().Replace(text, m => m.Groups[1].Value + char.ToUpperInvariant(m.Groups[2].Value[0]));

    [GeneratedRegex(@"\[[^\]]{1,40}\]|\((?:music|applause|laughs?|laughter|silence|inaudible|noise|coughs?|sighs?)\)", RegexOptions.IgnoreCase)]
    private static partial Regex BracketTag();
    [GeneratedRegex(@"(?<=\p{Ll}{2}[.!?])(?=\p{Lu}\p{Ll})")]
    private static partial Regex GluedSentence();
    [GeneratedRegex(@"\b(actually|no wait|wait no|scratch that|i mean|make that|or rather|never ?mind|sorry|correction|change that|on second thought)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CorrectionCue();
    [GeneratedRegex(@"[\p{L}\p{N}']+")]
    private static partial Regex Word();
    [GeneratedRegex(@"\*[^*]{1,30}\*")]
    private static partial Regex StarTag();
    [GeneratedRegex(@"[ \t]{2,}")]
    private static partial Regex MultiSpace();
    [GeneratedRegex(@"[\s,]+new paragraph[\s,.]+", RegexOptions.IgnoreCase)]
    private static partial Regex NewParagraph();
    [GeneratedRegex(@"[\s,]+new line[\s,.]+", RegexOptions.IgnoreCase)]
    private static partial Regex NewLine();
    [GeneratedRegex(@"[^.!?\n]*[,.]?\s*scratch that[,.]?", RegexOptions.IgnoreCase)]
    private static partial Regex ScratchThat();
    [GeneratedRegex(@"(?<=[\s,.]|^)(?:u+m+|u+h+|e+r+m+|h+m+|a+h+|mm+)(?=[\s,.!?]|$)[,]?", RegexOptions.IgnoreCase)]
    private static partial Regex Fillers();
    [GeneratedRegex(@"\b(?!(?:bye|very|really|now|go|knock|ha)\b)(\w{3,})(?:[\s,]+\1\b)+", RegexOptions.IgnoreCase)]
    private static partial Regex Stutter();
    [GeneratedRegex(@"\s+([,.!?:;])")]
    private static partial Regex SpaceBeforePunct();
    [GeneratedRegex(@"[ \t]*\n[ \t]*")]
    private static partial Regex SpaceAroundNewline();
    [GeneratedRegex(@"([,.!?:;])[,.;:]+")]
    private static partial Regex RepeatedPunct();
    [GeneratedRegex(@"^[\s,.;:]+")]
    private static partial Regex LeadingPunct();
    [GeneratedRegex(@"(^|[.!?]\s+|\n\s*)([a-z])")]
    private static partial Regex SentenceStart();
    [GeneratedRegex(@"(?<=^|\s)i(?=\s|'|$|[,.!?])")]
    private static partial Regex StandaloneI();
}
