using EnglishTutor.Core.Audio;
using EnglishTutor.Core.Speech;

namespace EnglishTutor.Console.Tests.Support;

// TTS for console tests: records (text, speed), returns a tiny clip and never reaches a real Piper.
// Behavior replaces the default answer (to throw, to wait for the token...); OnCall runs first.
public sealed class RecordingTextToSpeech : ITextToSpeech
{
    private static readonly AudioClip _clip = new(22050, new float[8]);

    public List<(string Text, float Speed)> Requests { get; } = [];

    public Action? OnCall { get; set; }

    public Queue<Func<CancellationToken, Task<AudioClip>>> Behaviors { get; } = new();

    public RecordingTextToSpeech Throws(Exception exception)
    {
        Behaviors.Enqueue(_ => throw exception);
        return this;
    }

    public async Task<AudioClip> SynthesizeAsync(string text, float speed, CancellationToken ct)
    {
        Requests.Add((text, speed));
        OnCall?.Invoke();
        return Behaviors.TryDequeue(out var behavior) ? await behavior(ct) : _clip;
    }
}
