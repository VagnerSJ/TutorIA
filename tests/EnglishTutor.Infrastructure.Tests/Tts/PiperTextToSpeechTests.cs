using System.Globalization;
using EnglishTutor.Infrastructure.Processes;
using EnglishTutor.Infrastructure.Tests.Fakes;
using EnglishTutor.Infrastructure.Tests.Support;
using EnglishTutor.Infrastructure.Tts;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace EnglishTutor.Infrastructure.Tests.Tts;

// US-011 / RN-TTS01 — adaptador Piper sobre o IProcessRunner falso (nenhum executável real)
public sealed class PiperTextToSpeechTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("piper-tests-").FullName;
    private readonly FakeProcessRunner _runner = new();
    private readonly FakeTimeProvider _time = new();
    private readonly PiperOptions _piperOptions;

    public PiperTextToSpeechTests()
    {
        _piperOptions = new PiperOptions
        {
            ExecutablePath = Path.Combine(_folder, "piper", "piper.exe"),
            ModelPath = Path.Combine(_folder, "piper", "voice.onnx"),
        };
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string TempFolder => Path.Combine(_folder, "tmp");

    private PiperTextToSpeech CreateSut()
    {
        Directory.CreateDirectory(TempFolder);
        return new PiperTextToSpeech(_runner, Options.Create(_piperOptions), _time, TempFolder);
    }

    private static string OutputFileOf(ProcessRequest request)
    {
        var index = request.Arguments.ToList().IndexOf("--output_file");
        return request.Arguments[index + 1];
    }

    // Piper "writes" the WAV the moment the fake process runs.
    private void PiperWrites(byte[] wav)
    {
        _runner.OnRun = request => File.WriteAllBytes(OutputFileOf(request), wav);
        _runner.ReturnsOutput(string.Empty);
    }

    // US-011 — apoio: adaptador Piper (WAV lido como clipe)
    [Fact]
    public async Task SynthesizeAsync_WithValidWav_ReturnsClipWithSampleRateAndNormalizedSamples()
    {
        PiperWrites(new WavBuilder().WithSampleRate(22050).WithSamples(16384, -16384, 0).Build());
        var sut = CreateSut();

        var clip = await sut.SynthesizeAsync("hello", 1.0f, CancellationToken.None);

        clip.SampleRate.Should().Be(22050);
        clip.Samples.ToArray().Should().Equal(0.5f, -0.5f, 0f);
    }

    // US-011 — apoio: adaptador Piper (invocação)
    [Fact]
    public async Task SynthesizeAsync_Always_RunsPiperWithModelAndOutputFileArguments()
    {
        PiperWrites(new WavBuilder().Build());
        var sut = CreateSut();

        await sut.SynthesizeAsync("hello", 1.0f, CancellationToken.None);

        var request = _runner.Requests.Should().ContainSingle().Which;
        request.FileName.Should().Be(_piperOptions.ExecutablePath);
        request.WorkingDirectory.Should().Be(Path.Combine(_folder, "piper"));
        request.Arguments.Should().HaveCount(6);
        request.Arguments.Take(4).Should().Equal("--model", _piperOptions.ModelPath, "--output_file", OutputFileOf(request));
        request.Arguments[4].Should().Be("--length_scale");
        Path.GetDirectoryName(OutputFileOf(request)).Should().Be(TempFolder);
        OutputFileOf(request).Should().EndWith(".wav");
    }

    // US-011 — apoio: velocidade vira length_scale = 1/velocidade
    [Theory]
    [InlineData(1.0f, "1")]
    [InlineData(0.8f, "1.25")]
    [InlineData(1.5f, "0.6667")]
    [InlineData(0.5f, "2")]
    [InlineData(1.2f, "0.8333")]
    public async Task SynthesizeAsync_WithSpeed_PassesInverseLengthScale(float speed, string expected)
    {
        PiperWrites(new WavBuilder().Build());
        var sut = CreateSut();

        await sut.SynthesizeAsync("hello", speed, CancellationToken.None);

        _runner.Requests.Single().Arguments[5].Should().Be(expected);
    }

    // US-011 — apoio: números com a cultura invariável (AP-054)
    [Fact]
    public async Task SynthesizeAsync_WhenCurrentCultureUsesComma_WritesLengthScaleWithDot()
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
        try
        {
            PiperWrites(new WavBuilder().Build());
            var sut = CreateSut();

            await sut.SynthesizeAsync("hello", 0.8f, CancellationToken.None);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        _runner.Requests.Single().Arguments[5].Should().Be("1.25");
    }

    // US-011 — apoio: o adaptador só impede divisão inválida (a faixa é do TtsOptionsValidator)
    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    public async Task SynthesizeAsync_WhenSpeedIsNotPositive_ThrowsWithExpectation(float speed)
    {
        var sut = CreateSut();

        var act = () => sut.SynthesizeAsync("hello", speed, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        exception.Which.Message.Should().Contain("speed must be greater than zero").And.Contain("Expected");
        _runner.Requests.Should().BeEmpty();
    }

    // US-011 — apoio: texto de uma linha vai com \n final (P5)
    [Fact]
    public async Task SynthesizeAsync_WhenTextIsSingleLine_SendsItWithTrailingNewline()
    {
        PiperWrites(new WavBuilder().Build());
        var sut = CreateSut();

        await sut.SynthesizeAsync("Nice to meet you, Ana.", 1.0f, CancellationToken.None);

        _runner.Requests.Single().StandardInput.Should().Be("Nice to meet you, Ana.\n");
    }

    // US-011 — apoio: quebras de linha viram um espaço (P5)
    [Theory]
    [InlineData("a\nb")]
    [InlineData("a\r\nb")]
    [InlineData("a\n\n  b")]
    public async Task SynthesizeAsync_WhenTextHasLineBreaks_SendsSingleLine(string text)
    {
        PiperWrites(new WavBuilder().Build());
        var sut = CreateSut();

        await sut.SynthesizeAsync(text, 1.0f, CancellationToken.None);

        _runner.Requests.Single().StandardInput.Should().Be("a b\n");
    }

    // US-011 — apoio: o WAV temporário é apagado depois do sucesso
    [Fact]
    public async Task SynthesizeAsync_AfterSuccess_DeletesTemporaryFile()
    {
        PiperWrites(new WavBuilder().Build());
        var sut = CreateSut();

        await sut.SynthesizeAsync("hello", 1.0f, CancellationToken.None);

        File.Exists(OutputFileOf(_runner.Requests.Single())).Should().BeFalse();
        Directory.GetFiles(TempFolder).Should().BeEmpty();
    }

    // US-011 — Cenário: falha da síntese não derruba a sessão (apoio: código de saída do Piper)
    [Fact]
    public async Task SynthesizeAsync_WhenPiperExitsWithError_ThrowsWithExitCodeAndStandardError()
    {
        _runner.Returns(new ProcessResult(3, string.Empty, "could not load voice"));
        var sut = CreateSut();

        var act = () => sut.SynthesizeAsync("hello", 1.0f, CancellationToken.None);

        var message = (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message;
        message.Should().Contain("Piper exited with code 3: could not load voice").And.Contain("Expected exit code 0");
    }

    // US-011 — Cenário: falha da síntese não derruba a sessão (apoio: Piper não gravou o arquivo)
    [Fact]
    public async Task SynthesizeAsync_WhenPiperWritesNoFile_ThrowsWithPath()
    {
        _runner.ReturnsOutput(string.Empty);
        var sut = CreateSut();

        var act = () => sut.SynthesizeAsync("hello", 1.0f, CancellationToken.None);

        var message = (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message;
        var path = OutputFileOf(_runner.Requests.Single());
        message.Should().Contain($"Piper did not produce an audio file at {path}").And.Contain("Expected a WAV file");
    }

    // US-011 — Cenário: falha da síntese não derruba a sessão (apoio: WAV inválido; o arquivo é apagado)
    [Fact]
    public async Task SynthesizeAsync_WhenWavIsInvalid_ThrowsWithExpectation()
    {
        PiperWrites(new WavBuilder().WithFormat(1, 2, 16).Build());
        var sut = CreateSut();

        var act = () => sut.SynthesizeAsync("hello", 1.0f, CancellationToken.None);

        var message = (await act.Should().ThrowAsync<InvalidDataException>()).Which.Message;
        message.Should().Contain("Expected PCM 16-bit mono");
        Directory.GetFiles(TempFolder).Should().BeEmpty();
    }

    // US-011 — Cenário: falha da síntese não derruba a sessão (apoio: limpeza em falha)
    [Fact]
    public async Task SynthesizeAsync_WhenPiperFails_DeletesTemporaryFile()
    {
        _runner.OnRun = request => File.WriteAllBytes(OutputFileOf(request), new WavBuilder().Build());
        _runner.Returns(new ProcessResult(1, string.Empty, "crashed after writing"));
        var sut = CreateSut();

        var act = () => sut.SynthesizeAsync("hello", 1.0f, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        Directory.GetFiles(TempFolder).Should().BeEmpty();
    }

    // US-011 — Cenário: Ctrl+C durante o áudio (o runner real mata a árvore: ProcessRunnerLiveTests)
    [Fact]
    public async Task SynthesizeAsync_WhenCancelled_CancelsRunnerTokenDeletesFileAndThrowsOperationCanceled()
    {
        using var caller = new CancellationTokenSource();
        _runner.OnRun = request => File.WriteAllBytes(OutputFileOf(request), new WavBuilder().Build());
        _runner.HangsUntilCancelled();
        var sut = CreateSut();
        var pending = sut.SynthesizeAsync("hello", 1.0f, caller.Token);

        await caller.CancelAsync();

        var act = () => pending.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var exception = await act.Should().ThrowAsync<OperationCanceledException>();
        exception.Which.Should().NotBeOfType<TimeoutException>();
        _runner.Tokens.Single().IsCancellationRequested.Should().BeTrue();
        Directory.GetFiles(TempFolder).Should().BeEmpty();
    }

    // US-011 — Cenário: falha da síntese não derruba a sessão (apoio: tempo máximo, ADR-008; lado "estourou")
    [Fact]
    public async Task SynthesizeAsync_WhenTimeoutElapses_ThrowsTimeoutAndCancelsRunnerToken()
    {
        using var caller = new CancellationTokenSource();
        _runner.HangsUntilCancelled();
        var sut = CreateSut();
        var pending = sut.SynthesizeAsync("hello", 1.0f, caller.Token);

        _time.Advance(TimeSpan.FromSeconds(_piperOptions.TimeoutSeconds));

        var act = () => pending.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var message = (await act.Should().ThrowAsync<TimeoutException>()).Which.Message;
        message.Should().Contain("did not finish within 30 seconds").And.Contain("Tts:Piper:TimeoutSeconds");
        caller.IsCancellationRequested.Should().BeFalse();
        _runner.Tokens.Single().IsCancellationRequested.Should().BeTrue();
    }

    // US-011 — apoio: tempo máximo, lado "dentro do tempo" (S1)
    [Fact]
    public async Task SynthesizeAsync_WhenFinishesBeforeTimeout_ReturnsClipAndDoesNotCancelRunnerToken()
    {
        var finish = new TaskCompletionSource<ProcessResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _runner.OnRun = request => File.WriteAllBytes(OutputFileOf(request), new WavBuilder().WithSampleRate(16000).Build());
        _runner.ReturnsWhenCompleted(finish.Task);
        var sut = CreateSut();
        var pending = sut.SynthesizeAsync("hello", 1.0f, CancellationToken.None);

        _time.Advance(TimeSpan.FromSeconds(_piperOptions.TimeoutSeconds - 1));
        finish.SetResult(new ProcessResult(0, string.Empty, string.Empty));

        var clip = await pending.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        clip.SampleRate.Should().Be(16000);
        _runner.Tokens.Single().IsCancellationRequested.Should().BeFalse();
    }
}
