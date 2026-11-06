using System.Text.RegularExpressions;
using DynamicIsland.Windows.Models;
using DynamicIsland.Windows.Services.Flow;

namespace DynamicIsland.Windows.Services.Q;

/// <summary>Strict transcript and real-audio validation for local Whisper wake listening.</summary>
public static partial class WhisperWakePolicy
{
    // Keep a small set of spelling variants produced for the unfamiliar name, but require
    // the complete greeting at the very beginning. Name-only and fuzzy edit-distance hits
    // are deliberately excluded from always-on listening.
    [GeneratedRegex(@"^\s*(?:hey|hay|hei)[\s,!.?:;\-]+(?:sutra|sutro|sootra|soutra|shutra|sutrah|sue[\s\-]+tra|su[\s\-]+tra)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LeadingPhrase();

    public static WakePhrase.Match CheckTranscript(string? transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript)) return new(false, "");
        var phrase = LeadingPhrase().Match(transcript);
        if (!phrase.Success) return new(false, "");
        var rest = transcript[phrase.Length..].Trim().TrimStart(',', '.', '!', '?', ';', ':', '-').Trim();
        return new(true, rest.Length == 0 ? "" : char.ToUpperInvariant(rest[0]) + rest[1..]);
    }

    public static WakePhrase.Match Check(string? transcript, float[] audio, int sampleRate,
        WakeVoiceProfile? profile, bool requireTraining)
    {
        var match = CheckTranscript(transcript);
        if (!match.Woke || (requireTraining && profile is not { IsValid: true })) return new(false, "");
        if (!TrySpeechRange(audio, sampleRate, out var start, out var length)) return new(false, "");
        // A trained template covers the phrase itself. Ask the question after the wake pause.
        // Without a profile, retain the cloud version's one-breath wake + command behavior.
        if (profile is not { IsValid: true }) return match;
        if (match.Command.Length != 0) return new(false, "");
        var features = Features(audio.AsSpan(start, length), sampleRate);
        return features is not null && WakeDetectionPolicy.Matches(profile, features, length / (double)sampleRate)
            ? match : new(false, "");
    }

    public static bool TryTrainingSample(string? transcript, float[] audio, int sampleRate,
        out double[] features, out double durationSeconds)
    {
        features = []; durationSeconds = 0;
        var match = CheckTranscript(transcript);
        if (!match.Woke || match.Command.Length != 0 || !TrySpeechRange(audio, sampleRate, out var start, out var length)) return false;
        durationSeconds = length / (double)sampleRate;
        if (durationSeconds is < .35 or > 3) return false;
        var result = Features(audio.AsSpan(start, length), sampleRate);
        if (result is null) return false;
        features = result;
        return true;
    }

    private static double[]? Features(ReadOnlySpan<float> audio, int sampleRate)
    {
        var pcm = new byte[audio.Length * 2];
        for (var i = 0; i < audio.Length; i++)
        {
            var value = (short)Math.Clamp((int)Math.Round(audio[i] * 32768), short.MinValue, short.MaxValue);
            pcm[i * 2] = (byte)value;
            pcm[i * 2 + 1] = (byte)(value >> 8);
        }
        return WakeDetectionPolicy.ExtractFeatures(pcm, sampleRate, 16, 1);
    }

    private static bool TrySpeechRange(float[] audio, int sampleRate, out int start, out int length)
    {
        start = length = 0;
        if (sampleRate is < 8000 or > 192000 || audio.Length < sampleRate * .35 || audio.Length > sampleRate * 7
            || audio.Any(x => !float.IsFinite(x))) return false;
        var frameSize = Math.Max(1, sampleRate / 50);
        var rms = new List<double>();
        double total = 0;
        var clipped = 0;
        for (var frame = 0; frame < audio.Length; frame += frameSize)
        {
            double energy = 0;
            var end = Math.Min(frame + frameSize, audio.Length);
            for (var i = frame; i < end; i++)
            {
                energy += audio[i] * audio[i];
                if (Math.Abs(audio[i]) >= .995) clipped++;
            }
            total += energy;
            rms.Add(Math.Sqrt(energy / (end - frame)));
        }
        if (Math.Sqrt(total / audio.Length) < .003 || clipped > audio.Length * .05) return false;
        var threshold = Math.Max(.003, rms.Max() * .12);
        var first = rms.FindIndex(x => x >= threshold);
        var last = rms.FindLastIndex(x => x >= threshold);
        if (first < 0) return false;
        // Remove VAD pre-roll and trailing silence consistently for training and matching.
        start = Math.Max(0, first * frameSize - sampleRate / 50);
        var finish = Math.Min(audio.Length, (last + 1) * frameSize + sampleRate / 50);
        length = finish - start;
        return length >= sampleRate * .35;
    }
}
