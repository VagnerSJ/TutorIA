---
name: bdd-para-testes
description: Como transformar cenários BDD (Dado/Quando/Então) das US em testes xUnit rastreáveis neste projeto. Use ao planejar ou escrever testes a partir de specs/sprints.
user-invocable: false
---

# De cenário BDD para teste

Não usamos SpecFlow/Reqnroll: os cenários viram testes xUnit comuns, com rastreabilidade explícita. Isso mantém a suíte simples e rápida.

## Mapeamento

| Gherkin | Teste |
|---|---|
| `Cenário: <nome>` | Um método (ou um `[Theory]` se houver tabela de exemplos) |
| `Dado ...` | Arrange: fakes, configuração, estado inicial |
| `Quando ...` | Act: **uma** chamada ao sistema sob teste |
| `Então ...` / `E ...` | Assert: uma asserção por linha do Então (FluentAssertions) |

## Nome e rastreabilidade

```csharp
// US-003 — Cenário: janela de turnos — RN-CONV03
[Fact]
public async Task SendAsync_WhenWindowIsFull_DropsOldestTurn()
```

- O comentário cita a US, o nome exato do cenário e as regras.
- O plano (`tasks/US-XXX/plan.md`, seção 4) lista cenário → classe → método. O Revisor confere essa tabela.

## Escolha do nível do teste

| O cenário fala de... | Teste em |
|---|---|
| Regra de conversa, janela, retentativa, modos | `EnglishTutor.Core.Tests` com fakes |
| Formato de resposta, saída do processo/HTTP | `EnglishTutor.Infrastructure.Tests` com fixtures |
| Comportamento comum a todos os cérebros | Suíte de contrato |
| Comportamento do console (comandos, mensagens) | Teste do componente de UI com `TextReader`/`TextWriter` falsos, sem processo real |
| Build, CI, estrutura | Teste de arquitetura (reflexão sobre assemblies) ou verificação manual registrada no review |

## Exemplo completo

```gherkin
Cenário: nova tentativa em resposta inválida
  Dado um cérebro que responde fora do formato na primeira vez e no formato correto na segunda
  Quando envio uma frase
  Então o cérebro é chamado 2 vezes
  E a segunda chamada inclui a instrução de correção de formato
```

```csharp
// US-003 — Cenário: nova tentativa em resposta inválida — RN-BRAIN04
[Fact]
public async Task SendAsync_WhenFirstReplyIsInvalid_RetriesWithFormatCorrection()
{
    var brain = new FakeTutorBrain()
        .ReturnsInvalid("no tags here", TutorReplyRejections.MissingReply())
        .Returns(TutorReplies.Valid("t1"));
    var sut = CreateService(brain);

    await sut.SendAsync("I goed to the park", CancellationToken.None);

    brain.Requests.Should().HaveCount(2);
    brain.Requests[1].Messages[^1].Content.Should().Contain("format");
}
```

## Exemplos tabulares

`Esquema do Cenário` / `Exemplos` vira `[Theory]` com `[InlineData]`, uma linha por exemplo.
