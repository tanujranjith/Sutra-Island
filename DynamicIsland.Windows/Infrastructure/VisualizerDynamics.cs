namespace DynamicIsland.Windows.Infrastructure;

/// <summary>
/// Loudness-aware scaling for the island visualizer.
/// </summary>
/// <remarks>
/// A fixed analyzer gain makes quiet songs look almost as large as loud songs. Scaling the
/// spectrum by a slow broadband-loudness envelope keeps loud passages expressive while letting
/// quiet passages settle into a smaller, calmer waveform.
/// </remarks>
public static class VisualizerDynamics
{
    private const double MinimumGain = 0.12;
    private const double ReferenceDecibels = -55;
    private const double RangeDecibels = 35;

    public static double MapLoudnessToGain(double rootMeanSquare)
    {
        if (double.IsNaN(rootMeanSquare) || rootMeanSquare <= 0)
            return MinimumGain;

        var decibels = 20 * Math.Log10(rootMeanSquare);
        return Math.Clamp((decibels - ReferenceDecibels) / RangeDecibels, MinimumGain, 1);
    }
}
