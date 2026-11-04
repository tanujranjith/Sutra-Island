namespace DynamicIsland.Windows.Services.Flow;

/// <summary>
/// A small energy-based voice detector for the assistant's always-on microphone. Feed it audio
/// frames; it tracks the room's noise floor, notices when someone starts talking, and hands
/// back the whole utterance (with a short lead-in) once they've been quiet long enough.
/// Pure and platform-free so it can be tested without a microphone.
/// </summary>
public sealed class VoiceSegmenter
{
    private readonly int _sampleRate;
    private readonly TimeSpan _endSilence;
    private readonly TimeSpan _maxLength;
    private readonly TimeSpan _minLength;
    private readonly List<float> _utterance = [];
    private readonly Queue<float[]> _preRoll = new();
    private int _preRollSamples;
    private int _speechFrames;
    private int _silentSamples;
    private bool _inSpeech;
    private double _noiseDb = -60;
    private long _totalSamples;

    /// <summary>Start speech this many dB above the noise floor.</summary>
    public double ThresholdDb { get; set; } = 10;
    /// <summary>Nothing quieter than this counts as speech, however quiet the room.</summary>
    public double FloorDb { get; set; } = -48;
    public bool InSpeech => _inSpeech;
    public double NoiseDb => _noiseDb;
    /// <summary>Seconds of audio seen so far.</summary>
    public double Elapsed => (double)_totalSamples / _sampleRate;

    public VoiceSegmenter(int sampleRate, TimeSpan endSilence, TimeSpan maxLength, TimeSpan? minLength = null)
    {
        _sampleRate = sampleRate;
        _endSilence = endSilence;
        _maxLength = maxLength;
        _minLength = minLength ?? TimeSpan.FromMilliseconds(250);
    }

    public static double DbOf(ReadOnlySpan<float> frame)
    {
        if (frame.Length == 0) return -100;
        double sum = 0;
        foreach (var s in frame) sum += s * s;
        return 20 * Math.Log10(Math.Max(Math.Sqrt(sum / frame.Length), 1e-5));
    }

    /// <summary>Adds one frame (about 30 ms). Returns a finished utterance, or null.</summary>
    public float[]? Push(ReadOnlySpan<float> frame)
    {
        _totalSamples += frame.Length;
        var db = DbOf(frame);
        var loud = db > Math.Max(_noiseDb + ThresholdDb, FloorDb);
        if (!_inSpeech)
        {
            // The floor falls quickly and rises slowly, so a burst of speech barely moves it.
            _noiseDb = db < _noiseDb ? _noiseDb + (db - _noiseDb) * 0.3 : _noiseDb + (db - _noiseDb) * 0.02;
            _preRoll.Enqueue(frame.ToArray());
            _preRollSamples += frame.Length;
            while (_preRollSamples > _sampleRate * 0.3 && _preRoll.Count > 1) _preRollSamples -= _preRoll.Dequeue().Length;
            _speechFrames = loud ? _speechFrames + 1 : 0;
            if (_speechFrames < 3) return null;
            _inSpeech = true;
            _silentSamples = 0;
            foreach (var f in _preRoll) _utterance.AddRange(f);
            _preRoll.Clear();
            _preRollSamples = 0;
            return null;
        }
        _utterance.AddRange(frame.ToArray());
        _silentSamples = loud ? 0 : _silentSamples + frame.Length;
        var tooLong = _utterance.Count >= _sampleRate * _maxLength.TotalSeconds;
        if (_silentSamples < _sampleRate * _endSilence.TotalSeconds && !tooLong) return null;
        var result = _utterance.ToArray();
        Reset();
        return result.Length >= _sampleRate * (_minLength.TotalSeconds + _endSilence.TotalSeconds) || tooLong ? result : null;
    }

    public void Reset()
    {
        _utterance.Clear();
        _inSpeech = false;
        _speechFrames = 0;
        _silentSamples = 0;
    }
}
