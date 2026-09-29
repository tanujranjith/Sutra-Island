namespace DynamicIsland.Windows.Infrastructure;

/// <summary>
/// Smooths spectrum targets into continuous Apple-style waveform motion.
/// </summary>
/// <remarks>
/// Apple’s island waveform does not jump between raw analyzer frames: bars breathe together,
/// treble decays quickly, and bass sustains a little longer. This helper lifts quiet bands,
/// applies independent attack/release smoothing, and layers distinct pulses across the bars.
/// </remarks>
public sealed class VisualizerMotion
{
    public const int BandCount = SpectrumAnalyzer.BandCount;

    private readonly double[] _levels = new double[BandCount];
    private readonly double[] _baseline = new double[BandCount];
    private bool _baselineInitialized;
    private double _phase;
    private bool _lively = true;

    public void Advance(ReadOnlySpan<double> targets, double deltaSeconds, bool lively = true)
    {
        if (targets.Length < BandCount)
            throw new ArgumentException($"The visualizer requires {BandCount} target levels.", nameof(targets));

        _lively = lively;
        if (!lively)
        {
            targets[..BandCount].CopyTo(_levels);
            return;
        }

        var elapsed = Math.Max(0, deltaSeconds);
        _phase += elapsed * Math.PI * 2.2;

        for (var band = 0; band < BandCount; band++)
        {
            var rawTarget = Math.Clamp(targets[band], 0, 1);
            if (!_baselineInitialized)
                _baseline[band] = rawTarget;

            // Keep a short resting height, then emphasize hits above this band's recent
            // level. A shared height curve buried quieter treble bands or pinned every bar
            // high for an entire song.
            var baselineRate = rawTarget > _baseline[band] ? 2.0 : 6.0;
            _baseline[band] += (rawTarget - _baseline[band]) * (1 - Math.Exp(-baselineRate * elapsed));
            var hit = Math.Max(0, rawTarget - _baseline[band]);
            var target = rawTarget <= 0.04 ? 0 : Math.Clamp(0.06 + (0.34 * rawTarget) + (5.5 * hit), 0, 1);
            var rate = target > _levels[band]
                ? 34 - (band * 0.7) // Reach an attack within a display frame.
                : 24 - (band * 0.5); // Drop back between beats instead of holding peaks.
            var blend = 1 - Math.Exp(-rate * elapsed);
            _levels[band] += (target - _levels[band]) * blend;
        }

        _baselineInitialized = true;
    }

    public double Level(int band)
    {
        if (band < 0 || band >= BandCount)
            throw new ArgumentOutOfRangeException(nameof(band));

        var level = _levels[band];
        if (!_lively)
            return Math.Clamp(level, 0, 1);

        var energy = 0.0;
        foreach (var value in _levels)
            energy += value;
        energy /= BandCount;

        // Three incommensurate oscillators per bar avoid mechanical uniform sine motion.
        var ripple =
            (0.55 * Math.Sin((_phase * (0.9 + (0.12 * band))) + (band * 1.7))) +
            (0.30 * Math.Sin((_phase * (1.7 + (0.23 * band))) + (band * 0.6))) +
            (0.15 * Math.Sin((_phase * (2.9 + (0.31 * band))) + (band * 2.9)));
        // The compact pill is only 28 pixels tall: sub-pixel ripples were effectively
        // invisible. Scale each bar's motion by its energy so silence remains still.
        ripple *= 0.4 * Math.Sqrt(level);

        // A shared beat-like swell ties the independent bars together without making them move
        // as one block.
        ripple += energy * 0.1 * Math.Sin((_phase * 0.9) + (band * 0.35));

        return Math.Clamp(level + ripple, 0, 1);
    }
}
