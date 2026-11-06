using System.Numerics;
using DynamicIsland.Windows.Models;

namespace DynamicIsland.Windows.Services.Q;

/// <summary>Phrase checks and a small local acoustic template matcher; this is not speaker authentication.</summary>
public static class WakeDetectionPolicy
{
    public const int RequiredSamples = 5;
    public const double MaximumEnrollmentDistance = .24;
    private const int Segments = 16, Coefficients = 12, SampleRate = 16000, FftSize = 512, MelBands = 20;

    public static bool IsPhrase(string? text, double confidence, IReadOnlyList<double> wordConfidences,
        double durationSeconds, double minimumConfidence = .86) =>
        string.Equals(text?.Trim(), "hey sutra", StringComparison.OrdinalIgnoreCase)
        && double.IsFinite(minimumConfidence) && minimumConfidence is >= .80 and <= .97
        && double.IsFinite(confidence) && confidence >= minimumConfidence
        && wordConfidences.Count == 2
        && wordConfidences.All(x => double.IsFinite(x) && x >= Math.Max(.68, minimumConfidence - .12))
        && double.IsFinite(durationSeconds) && durationSeconds is >= .35 and <= 3;

    public static WakeVoiceProfile CreateProfile(IReadOnlyList<double[]> examples, IReadOnlyList<double> durations)
    {
        if (examples.Count != RequiredSamples || durations.Count != RequiredSamples
            || examples.Any(x => !ValidTemplate(x))
            || durations.Any(x => !double.IsFinite(x) || x is < .35 or > 3))
            throw new InvalidOperationException("Five clear examples of Hey Sutra are needed. Try training again in a quiet room.");

        // Every sample must agree with at least two others. Enrollment retains good
        // candidates and requests replacements before calling this final validation.
        var nearestDistances = examples.Select((sample, i) => examples.Where((_, j) => i != j)
            .Select(other => Distance(sample, other)).Order().Take(2).Average()).ToArray();
        if (nearestDistances.Any(x => x > MaximumEnrollmentDistance))
            throw new InvalidOperationException("The examples sounded too different. Keep the microphone in the same place and try again.");
        return new WakeVoiceProfile
        {
            Version = 2,
            TrainedAtUtc = DateTimeOffset.UtcNow,
            SampleCount = RequiredSamples,
            Templates = examples.Select(x => x.ToArray()).ToArray(),
            MinimumDurationSeconds = Math.Max(.35, durations.Min() * .65),
            MaximumDurationSeconds = Math.Min(3, durations.Max() * 1.45),
            MaximumDistance = Math.Clamp(nearestDistances.Max() * 1.6 + .045, .10, .30)
        };
    }

    public static bool Matches(WakeVoiceProfile profile, double[] features, double durationSeconds)
    {
        if (!profile.IsValid || !ValidTemplate(features) || !double.IsFinite(durationSeconds)
            || durationSeconds < profile.MinimumDurationSeconds || durationSeconds > profile.MaximumDurationSeconds) return false;
        // Agreement with two examples prevents one accidental training outlier from opening the gate.
        return profile.Templates.Select(x => DistanceCore(features, x, normalizePath: profile.Version >= 2))
            .Order().Take(2).Average() <= profile.MaximumDistance;
    }

    private static bool ValidTemplate(double[]? features) => features is { Length: Segments * Coefficients }
        && features.All(double.IsFinite) && features.Any(x => Math.Abs(x) > .001);

    internal static bool ValidTrainingSample(double[]? features, double durationSeconds) =>
        ValidTemplate(features) && double.IsFinite(durationSeconds) && durationSeconds is >= .35 and <= 3;

    public static double Distance(double[] first, double[] second) => DistanceCore(first, second, normalizePath: true);

    private static double DistanceCore(double[] first, double[] second, bool normalizePath)
    {
        if (!ValidTemplate(first) || !ValidTemplate(second)) return double.PositiveInfinity;
        // Bounded dynamic time warping tolerates small changes in pronunciation speed.
        var previous = Enumerable.Repeat(double.PositiveInfinity, Segments + 1).ToArray();
        var previousLengths = new int[Segments + 1];
        previous[0] = 0;
        for (var i = 1; i <= Segments; i++)
        {
            var current = Enumerable.Repeat(double.PositiveInfinity, Segments + 1).ToArray();
            var currentLengths = new int[Segments + 1];
            for (var j = Math.Max(1, i - 3); j <= Math.Min(Segments, i + 3); j++)
            {
                double dot = 0, normA = 0, normB = 0;
                for (var k = 0; k < Coefficients; k++)
                {
                    var a = first[(i - 1) * Coefficients + k];
                    var b = second[(j - 1) * Coefficients + k];
                    dot += a * b; normA += a * a; normB += b * b;
                }
                var cost = 1 - Math.Clamp(dot / Math.Sqrt(Math.Max(1e-12, normA * normB)), -1, 1);
                var predecessor = previous[j - 1];
                var length = previousLengths[j - 1];
                if (previous[j] < predecessor || (previous[j] == predecessor && previousLengths[j] < length))
                { predecessor = previous[j]; length = previousLengths[j]; }
                if (current[j - 1] < predecessor || (current[j - 1] == predecessor && currentLengths[j - 1] < length))
                { predecessor = current[j - 1]; length = currentLengths[j - 1]; }
                current[j] = cost + predecessor;
                currentLengths[j] = length + 1;
            }
            previous = current;
            previousLengths = currentLengths;
        }
        // A warped alignment can contain more than sixteen comparisons. Divide by
        // the comparisons actually made, so normal timing changes are not penalized
        // simply for needing a longer path. Version 1 profiles retain their old metric.
        return previous[Segments] / (normalizePath ? previousLengths[Segments] : Segments);
    }

    /// <summary>Extracts normalized MFCC sequences from PCM audio; rejects silence and unusable captures.</summary>
    public static double[]? ExtractFeatures(byte[] pcm, int samplesPerSecond, int bitsPerSample, int channels)
    {
        if (bitsPerSample != 16 || channels is < 1 or > 2 || samplesPerSecond is < 8000 or > 192000) return null;
        var count = pcm.Length / (2 * channels);
        if (count < samplesPerSecond * .25 || count > samplesPerSecond * 4) return null;
        var mono = new double[count];
        double energy = 0;
        var clipped = 0;
        for (var i = 0; i < count; i++)
        {
            for (var channel = 0; channel < channels; channel++)
            {
                var p = (i * channels + channel) * 2;
                mono[i] += (short)(pcm[p] | pcm[p + 1] << 8) / (32768d * channels);
            }
            energy += mono[i] * mono[i];
            if (Math.Abs(mono[i]) >= .995) clipped++;
        }
        if (Math.Sqrt(energy / count) < .003 || clipped > count * .05) return null;
        var audio = new double[(int)((long)count * SampleRate / samplesPerSecond)];
        for (var i = 0; i < audio.Length; i++)
        {
            var position = i * (double)samplesPerSecond / SampleRate;
            var left = Math.Min((int)position, count - 1);
            var right = Math.Min(left + 1, count - 1);
            audio[i] = mono[left] + (mono[right] - mono[left]) * (position - left);
        }
        var frames = new List<(double Rms, double[] Coefficients)>();
        for (var start = 0; start + FftSize <= audio.Length; start += 160)
        {
            var fft = new Complex[FftSize];
            double frameEnergy = 0;
            for (var n = 0; n < FftSize; n++)
            {
                var sample = audio[start + n];
                frameEnergy += sample * sample;
                var emphasized = sample - .97 * (start + n > 0 ? audio[start + n - 1] : 0);
                fft[n] = emphasized * (.5 - .5 * Math.Cos(2 * Math.PI * n / (FftSize - 1)));
            }
            Transform(fft);
            var powers = fft.Take(FftSize / 2 + 1).Select(x => x.Magnitude * x.Magnitude).ToArray();
            var mel = new double[MelBands];
            var minMel = ToMel(80); var maxMel = ToMel(7600);
            for (var band = 0; band < MelBands; band++)
            {
                var low = FromMel(minMel + (maxMel - minMel) * band / (MelBands + 1));
                var mid = FromMel(minMel + (maxMel - minMel) * (band + 1) / (MelBands + 1));
                var high = FromMel(minMel + (maxMel - minMel) * (band + 2) / (MelBands + 1));
                double value = 0;
                for (var bin = (int)Math.Ceiling(low * FftSize / SampleRate); bin <= Math.Min(FftSize / 2, (int)(high * FftSize / SampleRate)); bin++)
                {
                    var frequency = bin * (double)SampleRate / FftSize;
                    var weight = frequency <= mid ? (frequency - low) / (mid - low) : (high - frequency) / (high - mid);
                    value += powers[bin] * Math.Max(0, weight);
                }
                mel[band] = Math.Log(Math.Max(1e-12, value));
            }
            var coefficients = new double[Coefficients];
            for (var c = 0; c < Coefficients; c++)
                for (var band = 0; band < MelBands; band++)
                    coefficients[c] += mel[band] * Math.Cos(Math.PI * (c + 1) * (band + .5) / MelBands);
            frames.Add((Math.Sqrt(frameEnergy / FftSize), coefficients));
        }
        if (frames.Count < Segments) return null;
        var threshold = Math.Max(.003, frames.Max(x => x.Rms) * .12);
        var first = frames.FindIndex(x => x.Rms >= threshold);
        var last = frames.FindLastIndex(x => x.Rms >= threshold);
        if (first < 0 || last - first + 1 < Segments) return null;
        var result = new double[Segments * Coefficients];
        for (var segment = 0; segment < Segments; segment++)
        {
            var start = first + segment * (last - first + 1) / Segments;
            var end = first + (segment + 1) * (last - first + 1) / Segments;
            for (var frame = start; frame < end; frame++)
                for (var c = 0; c < Coefficients; c++) result[segment * Coefficients + c] += frames[frame].Coefficients[c];
            var norm = Math.Sqrt(Enumerable.Range(0, Coefficients).Sum(c => Math.Pow(result[segment * Coefficients + c], 2)));
            if (norm < 1e-6) return null;
            for (var c = 0; c < Coefficients; c++) result[segment * Coefficients + c] /= norm;
        }
        return result;
    }

    private static double ToMel(double frequency) => 2595 * Math.Log10(1 + frequency / 700);
    private static double FromMel(double mel) => 700 * (Math.Pow(10, mel / 2595) - 1);
    private static void Transform(Complex[] values)
    {
        for (int i = 1, j = 0; i < values.Length; i++)
        {
            var bit = values.Length >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) (values[i], values[j]) = (values[j], values[i]);
        }
        for (var size = 2; size <= values.Length; size <<= 1)
        {
            var step = Complex.FromPolarCoordinates(1, -2 * Math.PI / size);
            for (var start = 0; start < values.Length; start += size)
            {
                var rotation = Complex.One;
                for (var j = 0; j < size / 2; j++)
                {
                    var even = values[start + j];
                    var odd = values[start + j + size / 2] * rotation;
                    values[start + j] = even + odd;
                    values[start + j + size / 2] = even - odd;
                    rotation *= step;
                }
            }
        }
    }
}
