namespace DynamicIsland.Windows.Infrastructure;

/// <summary>
/// Broadband loopback-audio spectrum analysis for the island visualizer.
/// </summary>
/// <remarks>
/// The visualizer needs frequency <em>bands</em>, not isolated probe tones. A single Goertzel
/// probe can miss most of the energy in a musical treble range, which is why the right-side
/// bars could collapse to their minimum height. This analyzer uses a Hann-windowed FFT and
/// integrates power across seven approximately logarithmic bands, then maps amplitudes with
/// conventional dBFS scaling.
/// </remarks>
public static class SpectrumAnalyzer
{
    public const int BandCount = 7;
    public const int WindowSize = 4096;
    private const int TransformSize = 4096;

    private const double FloorDecibels = -66;
    private const double RangeDecibels = 66;

    // Mean-square value of a Hann window. Dividing by this compensates for the energy removed
    // by windowing before estimating each band's RMS amplitude.
    private const double HannPowerGain = 0.375;

    private static readonly (double Low, double High)[] Bands =
    [
        (20, 120),
        (120, 300),
        (300, 700),
        (700, 1700),
        (1700, 4200),
        (4200, 9000),
        (9000, 19000),
    ];

    // Gentle perceptual weighting: music naturally carries less energy per hertz at high
    // frequencies. These gains keep treble visible without letting hiss dominate the display.
    private static readonly double[] BandGains = [0.75, 0.85, 0.95, 1.05, 1.2, 1.35, 1.55];

    public static double Analyze(ReadOnlySpan<float> samples, uint sampleRate, Span<double> levels)
    {
        if (samples.Length != WindowSize)
            throw new ArgumentException($"Spectrum analysis requires {WindowSize} samples.", nameof(samples));
        if (levels.Length < BandCount)
            throw new ArgumentException($"Spectrum analysis requires {BandCount} output levels.", nameof(levels));
        if (sampleRate == 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));

        Span<double> real = stackalloc double[TransformSize];
        Span<double> imaginary = stackalloc double[TransformSize];
        real.Clear();
        imaginary.Clear();

        var sumSquares = 0.0;
        for (var n = 0; n < WindowSize; n++)
        {
            sumSquares += samples[n] * samples[n];
            var window = 0.5 - (0.5 * Math.Cos((2 * Math.PI * n) / (WindowSize - 1)));
            real[n] = samples[n] * window;
        }

        ForwardTransform(real, imaginary);

        Span<double> bandPower = stackalloc double[BandCount];
        var binWidth = sampleRate / (double)TransformSize;

        // DC carries no useful musical level information and can otherwise pin the bass bar.
        for (var k = 1; k <= TransformSize / 2; k++)
        {
            var band = BandIndex(k * binWidth);
            if (band < 0)
                continue;

            bandPower[band] += (real[k] * real[k]) + (imaginary[k] * imaginary[k]);
        }

        for (var band = 0; band < BandCount; band++)
        {
            // Parseval's theorem relates the one-sided FFT power to time-domain mean-square
            // through the factor of two below.
            var meanSquare = (2 * bandPower[band]) / (WindowSize * TransformSize * HannPowerGain);
            var amplitude = Math.Sqrt(Math.Max(0, meanSquare));
            levels[band] = MapAmplitudeToLevel(amplitude * BandGains[band]);
        }

        return Math.Sqrt(sumSquares / WindowSize);
    }

    public static double MapAmplitudeToLevel(double amplitude)
    {
        if (double.IsNaN(amplitude) || amplitude <= 0)
            return 0;

        var decibels = 20 * Math.Log10(amplitude);
        return Math.Clamp((decibels - FloorDecibels) / RangeDecibels, 0, 1);
    }

    private static int BandIndex(double frequency)
    {
        for (var band = 0; band < BandCount; band++)
        {
            if (frequency >= Bands[band].Low && frequency < Bands[band].High)
                return band;
        }

        return -1;
    }

    private static void ForwardTransform(Span<double> real, Span<double> imaginary)
    {
        var n = real.Length;

        for (var i = 1; i < n; i++)
        {
            var j = 0;
            for (var bit = n >> 1; bit > 0; bit >>= 1)
                j = (j >> 1) | ((i & bit) != 0 ? n >> 1 : 0);

            if (i < j)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imaginary[i], imaginary[j]) = (imaginary[j], imaginary[i]);
            }
        }

        for (var size = 2; size <= n; size <<= 1)
        {
            var angle = (-2 * Math.PI) / size;
            var sizeCosine = Math.Cos(angle);
            var sizeSine = Math.Sin(angle);

            for (var i = 0; i < n; i += size)
            {
                var weightCosine = 1.0;
                var weightSine = 0.0;

                for (var j = 0; j < size / 2; j++)
                {
                    var evenReal = real[i + j];
                    var evenImaginary = imaginary[i + j];
                    var oddReal = real[i + j + (size / 2)];
                    var oddImaginary = imaginary[i + j + (size / 2)];

                    var weightedReal = (oddReal * weightCosine) - (oddImaginary * weightSine);
                    var weightedImaginary = (oddReal * weightSine) + (oddImaginary * weightCosine);

                    real[i + j] = evenReal + weightedReal;
                    imaginary[i + j] = evenImaginary + weightedImaginary;
                    real[i + j + (size / 2)] = evenReal - weightedReal;
                    imaginary[i + j + (size / 2)] = evenImaginary - weightedImaginary;

                    var nextCosine = (weightCosine * sizeCosine) - (weightSine * sizeSine);
                    weightSine = (weightCosine * sizeSine) + (weightSine * sizeCosine);
                    weightCosine = nextCosine;
                }
            }
        }
    }
}
