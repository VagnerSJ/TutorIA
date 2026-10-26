using System.Reflection;
using EnglishTutor.Core.Audio;

namespace EnglishTutor.Core.Tests.Architecture;

// US-010 — Cenário: portas de voz existem no Core sem pacote externo — R1, R4
public sealed class VoicePortsTests
{
    private static readonly Assembly _core = typeof(AudioClip).Assembly;

    [Theory]
    [InlineData("EnglishTutor.Core.Speech.ISpeechToText", true)]
    [InlineData("EnglishTutor.Core.Speech.ITextToSpeech", true)]
    [InlineData("EnglishTutor.Core.Audio.IAudioRecorder", true)]
    [InlineData("EnglishTutor.Core.Audio.IAudioPlayer", true)]
    [InlineData("EnglishTutor.Core.Audio.AudioClip", false)]
    [InlineData("EnglishTutor.Core.Speech.Transcript", false)]
    public void CoreAssembly_Always_ExposesVoiceType(string fullName, bool isInterface)
    {
        var type = _core.GetType(fullName);

        type.Should().NotBeNull();
        type!.IsPublic.Should().BeTrue();
        type.IsInterface.Should().Be(isInterface);
        type.IsSealed.Should().Be(!isInterface);
    }

    [Theory]
    [InlineData("EnglishTutor.Core.Speech.ISpeechToText")]
    [InlineData("EnglishTutor.Core.Speech.ITextToSpeech")]
    [InlineData("EnglishTutor.Core.Audio.IAudioRecorder")]
    [InlineData("EnglishTutor.Core.Audio.IAudioPlayer")]
    public void VoicePorts_AsyncMethods_TakeCancellationTokenAsLastParameter(string portName)
    {
        var port = _core.GetType(portName);
        port.Should().NotBeNull();

        var methods = port!.GetMethods();

        methods.Should().NotBeEmpty();
        methods.Should().OnlyContain(method => method.GetParameters().Last().ParameterType == typeof(CancellationToken));
    }
}
