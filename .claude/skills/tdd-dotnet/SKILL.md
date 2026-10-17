---
name: tdd-dotnet
description: Como escrever testes neste projeto .NET (xUnit, FluentAssertions, NSubstitute, fakes, fixtures, testes de contrato, cobertura) seguindo Red → Green → Refactor. Use ao escrever ou revisar testes C#.
user-invocable: false
---

# TDD em .NET neste projeto

## Pilha

- **xUnit** (v3 se disponível para o SDK em uso; senão v2) — `[Fact]`, `[Theory]` + `[InlineData]`/`[MemberData]`.
- **FluentAssertions** — `result.Should().Be(...)`, `.BeEquivalentTo(...)`, `await act.Should().ThrowAsync<BrainException>()`.
- **NSubstitute** — dublês de interfaces quando é preciso verificar interação (`Received(1)`).
- **coverlet.collector** — cobertura: `dotnet test --collect:"XPlat Code Coverage"`.

> Se a licença da versão mais nova do FluentAssertions for um problema, use a última 7.x (Apache 2.0) e registre no plano.

## Organização

```
tests/EnglishTutor.Core.Tests/
  Conversation/ConversationServiceTests.cs     # espelha src/EnglishTutor.Core/Conversation/
  Fakes/FakeTutorBrain.cs                      # fakes reutilizáveis
tests/EnglishTutor.Infrastructure.Tests/
  Brain/TutorReplyParserTests.cs
  Contract/TutorBrainContractTests.cs          # abstrata
  Contract/ClaudeCodeTutorBrainContractTests.cs
  Fixtures/claude-json-structured.json         # saídas reais gravadas a partir da US-004 (CopyToOutputDirectory)
```

- Uma classe de teste por classe de produção; nome `<Classe>Tests`.
- Método: `Metodo_Cenario_ResultadoEsperado` (ex.: `SendAsync_WhenBrainReturnsInvalidFormatTwice_ReturnsRawFallback`).
- Cite a regra/US no topo do teste: `// US-003 / RN-BRAIN04`.
- Estrutura Arrange / Act / Assert separada por linha em branco (sem comentários `// Arrange`).

## Fakes x mocks

- Prefira **fakes escritos à mão** para portas com comportamento (ex.: `FakeTutorBrain` com fila de respostas e lista de `BrainRequest` recebidos) — os testes ficam legíveis e verificam estado.
- Use NSubstitute para interações simples ou para forçar exceções.
- Nunca mocke tipos que você não controla (ex.: `HttpClient`, `Process`); envolva-os numa porta (`IProcessRunner`, `HttpMessageHandler` fake).

```csharp
public sealed class FakeTutorBrain : ITutorBrain
{
    private readonly Queue<Func<BrainRequest, TutorReply>> _responses = new();
    public List<BrainRequest> Requests { get; } = [];
    public string Name => "Fake";

    public FakeTutorBrain Returns(TutorReply reply) { _responses.Enqueue(_ => reply); return this; }
    public FakeTutorBrain Throws(Exception ex) { _responses.Enqueue(_ => throw ex); return this; }

    public Task<TutorReply> SendAsync(BrainRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        return Task.FromResult(_responses.Dequeue()(request));
    }
}
```

## Ciclo

1. **Red**: primeiro crie os esqueletos mínimos que o teste vai usar (arquivos, tipos e membros que lançam `NotImplementedException`, `using`s); só então escreva o teste do cenário. Assim o hook pós-edição compila a cada passo, e o Red falha **na asserção ou na exceção prevista**, nunca por erro de compilação (AP-013). Commit `test(...)` com teste e esqueletos.
2. **Green**: o mínimo para passar. Commit `feat(...)`.
3. **Refactor**: com tudo verde. Commit `refactor(...)`.

## Assíncrono e cancelamento

- Todo teste assíncrono é `async Task`; nunca `.Result`/`.Wait()`.
- Passe `TestContext.Current.CancellationToken` (xUnit v3) ou `CancellationToken.None` (v2).
- Cubra pelo menos um cenário de cancelamento/timeout por porta que fala com processo ou rede.

## Testes de contrato

```csharp
public abstract class TutorBrainContractTests
{
    protected abstract ITutorBrain CreateBrain(/* respostas simuladas */);

    [Fact]
    public async Task SendAsync_WithValidOutput_ReturnsReplyEndingWithQuestion() { ... }
}

public sealed class OllamaTutorBrainContractTests : TutorBrainContractTests { ... }
```

Variantes live: subclasses marcadas com `[Trait("Category", "LiveBrain")]`, excluídas no CI com `--filter "Category!=LiveBrain"`.

## Fixtures

Saídas reais de processo/HTTP gravadas em `Fixtures/*.json` (a partir da US-004; a US-002 usa exemplos da spec em `Fixtures/TutorReply/`), com `<None Include="Fixtures\**" CopyToOutputDirectory="PreserveNewest" />`. Nunca dados pessoais nem tokens.

## Cobertura

Meta: ≥ 80% de linhas em `EnglishTutor.Core`. Gere com `dotnet test --collect:"XPlat Code Coverage"` e leia `line-rate` do pacote `EnglishTutor.Core` no `coverage.cobertura.xml`.
