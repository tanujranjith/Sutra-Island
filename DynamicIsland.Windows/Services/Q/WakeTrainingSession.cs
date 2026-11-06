using DynamicIsland.Windows.Models;

namespace DynamicIsland.Windows.Services.Q;

public sealed record WakeTrainingUpdate(int AcceptedCount, bool SampleAccepted, bool IsComplete);

/// <summary>Retains a coherent set of spoken examples while asking for outlier replacements.</summary>
public sealed class WakeTrainingSession
{
    private const int MaximumCandidates = 15;
    private sealed record Example(double[] Features, double DurationSeconds);
    private readonly List<Example> _candidates = [];
    private List<Example> _selected = [];
    public int AcceptedCount => _selected.Count;
    public bool IsComplete => AcceptedCount == WakeDetectionPolicy.RequiredSamples;

    public WakeTrainingUpdate Add(double[] features, double durationSeconds)
    {
        if (IsComplete || !WakeDetectionPolicy.ValidTrainingSample(features, durationSeconds))
            return new(AcceptedCount, false, IsComplete);
        if (_candidates.Count == MaximumCandidates)
        {
            // Keep the strongest existing examples. In particular, a poor first
            // capture must not become an anchor that every later capture must match.
            var discarded = _candidates.FindIndex(x => !_selected.Contains(x));
            if (discarded >= 0) _candidates.RemoveAt(discarded);
        }
        var candidate = new Example(features.ToArray(), durationSeconds);
        _candidates.Add(candidate);
        SelectCoherentExamples();
        return new(AcceptedCount, _selected.Contains(candidate), IsComplete);
    }

    public bool TryCreateProfile(out WakeVoiceProfile? profile)
    {
        profile = IsComplete ? WakeDetectionPolicy.CreateProfile(_selected.Select(x => x.Features).ToArray(),
            _selected.Select(x => x.DurationSeconds).ToArray()) : null;
        return profile is not null;
    }

    private void SelectCoherentExamples()
    {
        var count = _candidates.Count;
        var distances = new double[count, count];
        for (var i = 0; i < count; i++)
            for (var j = i + 1; j < count; j++)
                distances[i, j] = distances[j, i] = WakeDetectionPolicy.Distance(_candidates[i].Features, _candidates[j].Features);
        for (var size = Math.Min(WakeDetectionPolicy.RequiredSamples, count); size > 0; size--)
        {
            var combination = new int[size];
            int[]? best = null;
            var bestScore = double.PositiveInfinity;
            Search(0, 0);
            if (best is null) continue;
            _selected = best.Select(i => _candidates[i]).ToList();
            return;

            void Search(int depth, int first)
            {
                if (depth < size)
                {
                    for (var i = first; i <= count - (size - depth); i++)
                    { combination[depth] = i; Search(depth + 1, i + 1); }
                    return;
                }
                double score = 0;
                foreach (var index in combination)
                {
                    var neighbors = combination.Where(other => other != index).Select(other => distances[index, other]).Order().Take(2).ToArray();
                    var distance = neighbors.Length == 0 ? 0 : neighbors.Average();
                    if (distance > WakeDetectionPolicy.MaximumEnrollmentDistance) return;
                    score += distance;
                }
                if (score >= bestScore) return;
                bestScore = score;
                best = combination.ToArray();
            }
        }
    }
}
