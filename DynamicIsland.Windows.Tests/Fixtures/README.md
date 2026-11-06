# Synthetic wake phrase test fixture

`hey-sutra-synthetic.wav` contains computer-generated speech, not microphone audio or a user's voice.

- Text: `Hey Sutra`
- Generator: Windows `System.Speech.Synthesis.SpeechSynthesizer`
- Voice: Microsoft David Desktop (Windows default in the generation environment)
- Rate: 0; default volume
- Format: RIFF/WAVE, PCM signed 16-bit mono, 22,050 Hz
- SHA-256: `BB7F93725AE474AA836339DB600C056087FF5A5796DD005AB8E0FC9312D753AF`

Generation used `SetOutputToWaveFile` and `Speak`; it did not play audio or use a microphone. The fixture is embedded only in the test assembly. Tests deterministically vary timing, gain, filtering, and noise to exercise enrollment and matching without Windows speech services.
