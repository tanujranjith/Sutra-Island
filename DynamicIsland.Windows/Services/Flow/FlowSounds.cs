using NAudio.Wave;

namespace DynamicIsland.Windows.Services.Flow;

/// <summary>
/// Soft dictation cues, synthesised in memory (no sound files): a rising two-note blip when
/// listening starts, a falling one when it stops, and a low note on an error. Kept short and quiet
/// so the start cue barely reaches the microphone.
/// </summary>
public sealed class FlowSounds(LoggingService log)
{
    private const int Rate = 44100;
    private static readonly Lazy<byte[]> Start = new(() => Render((660, 45), (990, 60)));
    private static readonly Lazy<byte[]> Stop = new(() => Render((880, 45), (590, 60)));
    private static readonly Lazy<byte[]> Error = new(() => Render((300, 90)));

    public void PlayStart() => Play(Start.Value);
    public void PlayStop() => Play(Stop.Value);
    public void PlayError() => Play(Error.Value);

    private void Play(byte[] pcm) => Task.Run(() =>
    {
        try
        {
            var stream = new RawSourceWaveStream(new MemoryStream(pcm), new WaveFormat(Rate, 16, 1));
            var output = new WaveOutEvent { DesiredLatency = 60 };
            output.PlaybackStopped += (_, _) => { output.Dispose(); stream.Dispose(); };
            output.Init(stream);
            output.Play();
        }
        catch (Exception ex) { log.Debug($"Sutra Flow: cue skipped ({ex.Message})."); }
    });

    /// <summary>Sine notes (Hz, ms) with a short fade at each end, at about 12% volume.</summary>
    private static byte[] Render(params (double Hz, int Ms)[] notes)
    {
        var samples = new List<short>();
        foreach (var (hz, ms) in notes)
        {
            var n = Rate * ms / 1000;
            var fade = Math.Min(n / 3, Rate * 8 / 1000);
            for (var i = 0; i < n; i++)
            {
                var env = Math.Min(1.0, Math.Min(i, n - 1 - i) / (double)fade);
                samples.Add((short)(Math.Sin(2 * Math.PI * hz * i / Rate) * env * 0.12 * short.MaxValue));
            }
        }
        var bytes = new byte[samples.Count * 2];
        Buffer.BlockCopy(samples.ToArray(), 0, bytes, 0, bytes.Length);
        return bytes;
    }
}
