using System.Text.Json.Serialization;

namespace DynamicIsland.Windows.Models;

/// <summary>Local acoustic examples of the wake phrase. Contains features, never recorded audio.</summary>
public sealed class WakeVoiceProfile
{
    public int Version { get; set; } = 1;
    public DateTimeOffset TrainedAtUtc { get; set; }
    public int SampleCount { get; set; }
    public double[][] Templates { get; set; } = [];
    public double MinimumDurationSeconds { get; set; }
    public double MaximumDurationSeconds { get; set; }
    public double MaximumDistance { get; set; }

    [JsonIgnore]
    public bool IsValid => Version is 1 or 2 && SampleCount == 5 && Templates is { Length: 5 }
        && Templates.All(x => x is { Length: 192 } && x.All(double.IsFinite)
            && x.Any(v => Math.Abs(v) > .001))
        && double.IsFinite(MinimumDurationSeconds) && MinimumDurationSeconds >= .35
        && double.IsFinite(MaximumDurationSeconds) && MaximumDurationSeconds <= 3
        && MinimumDurationSeconds < MaximumDurationSeconds
        && double.IsFinite(MaximumDistance) && MaximumDistance is >= .08 and <= .32;
}
