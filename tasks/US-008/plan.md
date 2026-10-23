# Plano — US-008 Suíte de contrato do ITutorBrain

Status: Aprovado em 2026-10-23
Branch: feature/US-008_contrato-itutorbrain
Spec: specs/sprints/SPRINT_1.md#us-008
Regras: RN-BRAIN01, RN-BRAIN03 (da US); RN-BRAIN04, RN-BRAIN07, RN-BRAIN09 (tocadas pelos testes, sem mudança); ARCHITECTURE §3, §10; ADR-005, ADR-006, ADR-007, ADR-009 (como consumidoras)

## 1. Objetivo

Uma única classe abstrata `TutorBrainContractTests`, executada por uma subclasse por provedor (`ClaudeCode`, `Ollama`) com fakes de processo e de cliente de chat, de modo que trocar de provedor não mude o comportamento visto pela aplicação; mais classes `LiveBrain` de conformidade contra os provedores reais e um teste de guarda que garante que elas ficam fora de `Category!=LiveBrain`.

## 2. Abordagem

Esta US **não cria nem altera código de produção** (`src/` não muda). Tudo é código de teste em `tests/EnglishTutor.Infrastructure.Tests/Contract/`. Não há ADR novo: a estrutura (classe abstrata + uma subclasse por provedor + variante live) já está fixada em `ARCHITECTURE.md` §10; o que muda é só a redação da linha "Live" (§2.6).

### 2.1 Forma da suíte (o que é abstrato)

O §10 fala em "fake de processo/HTTP", mas os dois adaptadores têm portas de falha diferentes (`IProcessRunner` e `IChatClient`). A suíte abstrai isso numa **bancada** por provedor, para o corpo dos testes ser único:

```csharp
public abstract class BrainContractHarness : IDisposable
{
    public const int TimeoutSeconds = 1;                  // Options de teste dos dois adaptadores
    public abstract string ProviderName { get; }          // BrainProviders.ClaudeCode | Ollama
    public abstract ITutorBrain Brain { get; }            // adaptador REAL sobre o fake, já criado no modo pedido
    public abstract BrainErrorKind UnavailableKind { get; }      // ClaudeCode: CliNotFound; Ollama: Unknown (ADR-009, limite L3 da US-007)
    public abstract string UnavailableSettingKey { get; }        // "Brain:ClaudeCode:ExecutablePath" | "Brain:Ollama:Endpoint"
    public abstract void EnqueueModelText(string modelText);     // próxima resposta do "modelo"
    public abstract void EnqueueHang();                          // só termina por cancelamento
    public abstract void EnqueueUnavailable();                   // processo/servidor inalcançável
    public abstract Task CallStarted { get; }                    // completa quando o fake recebe a 1ª chamada
    public abstract void Advance(TimeSpan time);                 // FakeTimeProvider do adaptador
    public abstract IReadOnlyList<SentToModel> Sent { get; }     // uma entrada por chamada ao modelo
}
public sealed record SentToModel(string SystemPrompt, string Conversation);

public abstract class TutorBrainContractTests : IDisposable
{
    protected abstract BrainContractHarness CreateHarness(string outputMode);   // BrainOutputModes.JsonSchema | Tags
}
```

- `ClaudeCodeContractHarness`: `ClaudeCodeTutorBrain` real + `FakeProcessRunner` + `FakeTimeProvider` + pasta de trabalho temporária + `PromptFiles` de `AppContext.BaseDirectory/prompts` (como `ClaudeCodeTutorBrainTests`). `EnqueueModelText`: se o texto é um objeto JSON válido **e** o modo é `JsonSchema`, devolve `ClaudeOutputs.Structured`; caso contrário `ClaudeOutputs.Text` (um JSON truncado ou texto cru cai no segundo caminho, e o `RawText` volta idêntico). `EnqueueUnavailable`: `Throws(new Win32Exception(2))`. `Sent`: `SystemPrompt` lido do arquivo temporário em `OnRun` (existe só durante a chamada), `Conversation` = `StandardInput`.
- `OllamaContractHarness`: `OllamaTutorBrain` real + `FakeChatClient` + `OllamaTestKit.CreatePromptFiles` + `FakeTimeProvider`. `EnqueueModelText` = `Returns(texto)`. `EnqueueUnavailable`: `Throws(new HttpRequestException("connection refused t1"))`. `Sent`: `SystemPrompt` = texto da mensagem `System`; `Conversation` = demais mensagens unidas por `\n`.
- Subclasses concretas (`ClaudeCodeTutorBrainContractTests`, `OllamaTutorBrainContractTests`) só implementam `CreateHarness`; **não declaram testes** (guarda G2).
- `Sent.Conversation` serve a asserções "contém, em ordem" (`IndexOf` crescente). Não se compara o texto inteiro, porque o Claude Code renderiza papéis (`ClaudeCodePromptRenderer`) e o Ollama manda mensagens separadas; o contrato é "todo conteúdo da janela chega ao modelo, na ordem".

Alternativas descartadas: (a) `CreateBrain(respostas)` do exemplo da skill: não dá para enfileirar falhas diferentes por provedor (texto atualizado em §2.6); (b) NSubstitute no lugar da bancada: perde a verificação de estado (skill `tdd-dotnet`); (c) rodar os mesmos testes contra o modelo real: o modelo real não produz timeout, resposta inválida nem inalcançável sob demanda.

### 2.2 O que o contrato cobre, e o que não cobre

Cobre o que **os dois** provedores prometem à aplicação: nome canônico; mesmo `TutorReply` nos dois formatos de saída (RN-BRAIN03, parser único); mesma tolerância de perguntas (0 e 3 rejeitadas, 1 e 2 aceitas; o ADR-005 pede que a suíte confira que o adaptador usa o parser); saída inutilizável vira `BrainException.InvalidOutput` com `Rejection` e `RawText` (ADR-006); janela completa e sem estado entre chamadas (RN-BRAIN01); `Timeout` (RN-BRAIN09); inalcançável vira `BrainException` com o tipo próprio do provedor (RN-BRAIN07); cancelamento propaga; retentativa única pelo `ConversationService` funciona igual (RN-BRAIN04).

Não cobre (cada item continua em teste do adaptador): `NotAuthenticated` e `UsageLimitReached` (só o Claude Code os produz, US-005); argumentos do processo, `--resume` em detalhe (ADR-007), `format` do Ollama (US-004/US-006); texto exato das mensagens de falha. Isso reverte em parte a nota da US-005 §8 ("classificação de falhas na suíte de contrato: não prevista"): entram só as duas falhas que os dois provedores produzem, Timeout e inalcançável (P4), e a mensagem é conferida só pela chave de configuração que ela cita. O `FallbackTutorBrain` não é provedor e fica fora (US-007 §8).

### 2.3 Resume do Claude Code e RN-BRAIN01

O Claude Code envia só a última mensagem quando o pedido estende a sessão conhecida (ADR-007); o Ollama sempre manda a janela inteira. Por isso: a janela completa é conferida com **instância nova** (K6, K7); o isolamento entre chamadas, com janelas **sem relação** (K8); e o caso "pedido que estende o anterior" confere o **resultado** (resposta correta e mensagem nova presente), não o texto inteiro (K9).

### 2.4 Variante live

`ClaudeCodeContractLiveTests` e `OllamaContractLiveTests` (classes `sealed`, **sem** herdar da suíte de fakes), cada uma com `[Trait("Category","LiveBrain")]` e **um** teste: cria o `ConversationService` real sobre o adaptador real (como `OllamaConversationLiveTests`), envia dois turnos (`LiveClaudeCode.KnownErrors[0]` e `[1]`, o segundo com histórico) e confere, em cada resposta, as asserções comuns de `Support/LiveContractChecks`: não é `IsRawFallback`; `TutorReplyValidator.Validate` aceita; no máximo 2 correções; `RawText` não vazio; e `brain.Name` é o nome canônico. Nomes escolhidos para **não** conter a subcadeia `ContractTests` (o D5 da spec filtra por ela e não pode arrastar testes `LiveBrain`).

Critério `LiveBrain` (AP-032, AP-052): camada = `ConversationService` real com a retentativa única (ADR-006) sobre o adaptador real; tolerância = **zero** respostas cruas nos 2 turnos; passar em **3 rodadas seguidas**; a retentativa **não** precisa ter sido exercitada (quem prova o mecanismo é K15); nenhuma métrica é só registrada. Risco de oscilação e dono da decisão em §7.

### 2.5 Guardas

- G1/G2 (`ContractSuiteStructureTests`): para cada provedor de `BrainProviders.All` existe `<Provedor>TutorBrainContractTests` concreta que herda da base; nenhuma subclasse declara `[Fact]`/`[Theory]` (a suíte é a mesma). Um provedor novo em `BrainProviders.All` sem subclasse deixa a suíte vermelha.
- G3–G5 (`LiveBrainTraitTests`): toda classe de teste cujo nome termina em `LiveTests` tem o trait; nenhuma classe sem esse sufixo tem o trait; nenhuma classe da suíte de contrato tem o trait. Isso prova o cenário "testes live opcionais" por reflexão; a DoD D7 prova o comportamento do filtro.

### 2.6 Alterações em outros documentos (dois commits do orquestrador antes da T1)

Cada trecho é identificado pelo texto antigo (AP-023). Os comandos de busca foram rodados antes (AP-035): `Grep "mesmas classes|TutorBrainContractTests|Contract/"` achou `ARCHITECTURE.md` (linhas 271–272, e 82 sem mudança), `.claude/skills/tdd-dotnet/SKILL.md` (linhas 26–27, 87–98) e `specs/sprints/SPRINT_1.md` (494); `CLAUDE.md`: nenhuma ocorrência. Não há renomeação de cenário.

**Commit 1 — `docs(ai): update spec for US-008`** (`specs/sprints/SPRINT_1.md`, seção `## US-008`):

(a) Regras. Texto antigo: `**Regras:** RN-BRAIN01, RN-BRAIN03; ARCHITECTURE §10.` Texto novo: `**Regras:** RN-BRAIN01, RN-BRAIN03, RN-BRAIN04, RN-BRAIN07, RN-BRAIN09; ARCHITECTURE §10.`

(b) DoD extra. Depois da linha antiga `| D5 | `dotnet test --filter "FullyQualifiedName~ContractTests" --logger "console;verbosity=normal"` | os mesmos testes do contrato listados para ClaudeCode e Ollama, todos verdes |`, acrescentar (critérios observáveis reutilizados: "verde", código de saída, contagem):

```
| D6 | `dotnet test --filter "FullyQualifiedName~ClaudeCodeTutorBrainContractTests"` e o mesmo com `OllamaTutorBrainContractTests` | verde, com o mesmo número de testes nos dois (o contrato inteiro roda por provedor) |
| D7 | (Git Bash) `dotnet test --no-build --filter "Category!=LiveBrain" --list-tests \| grep -c "ContractLiveTests"` e `dotnet test --no-build --filter "Category=LiveBrain" --list-tests \| grep -c "ContractLiveTests"` | `0` no primeiro (código de saída 1 com contagem 0 é o esperado); pelo menos `2` no segundo: os testes live não entram em `Category!=LiveBrain` e aparecem em `Category=LiveBrain` |
| D8 | `dotnet test --filter "Category=LiveBrain&FullyQualifiedName~ContractLiveTests"`, com a CLI do Claude Code autenticada e o Ollama rodando com o modelo padrão baixado | verde em 3 rodadas seguidas: cada provedor responde 2 turnos pelo `ConversationService` sem cair na resposta crua (a retentativa única do ADR-006 vale) |
```

**Commit 2 — `docs(ai): update architecture for US-008`** (`ARCHITECTURE.md` §10 e `.claude/skills/tdd-dotnet/SKILL.md`):

1. `ARCHITECTURE.md` §10. Antigo: `| Contrato | `Infrastructure.Tests/Contract/TutorBrainContractTests` (classe abstrata) | uma subclasse por provedor; fake de processo/HTTP |`. Novo: `| Contrato | `Infrastructure.Tests/Contract/TutorBrainContractTests` (classe abstrata) | uma subclasse por provedor (`ClaudeCodeTutorBrainContractTests`, `OllamaTutorBrainContractTests`), sem testes próprios; fake de processo/HTTP por trás de uma bancada por provedor |`.
2. `ARCHITECTURE.md` §10. Antigo: `| Live | mesmas classes com `[Trait("Category","LiveBrain")]` | excluídas por padrão: `dotnet test --filter "Category!=LiveBrain"` |`. Novo: `| Live | classes `*LiveTests` (as de contrato: `<Provedor>ContractLiveTests`, com o `ConversationService` real sobre o adaptador real) com `[Trait("Category","LiveBrain")]` | excluídas por padrão: `dotnet test --filter "Category!=LiveBrain"` |`.
3. `.claude/skills/tdd-dotnet/SKILL.md` (repete a mesma regra, AP-005). Antigo: `protected abstract ITutorBrain CreateBrain(/* respostas simuladas */);`. Novo: `protected abstract BrainContractHarness CreateHarness(string outputMode);   // bancada: adaptador real sobre o fake do provedor`.
4. `.claude/skills/tdd-dotnet/SKILL.md`. Antigo: `Variantes live: subclasses marcadas com `[Trait("Category", "LiveBrain")]`, excluídas no CI com `--filter "Category!=LiveBrain"`.` Novo: `Variantes live: classes `<Provedor>ContractLiveTests` (não herdam da suíte de contrato, cujos fakes simulam falhas que o provedor real não provoca), marcadas com `[Trait("Category", "LiveBrain")]` e excluídas no CI com `--filter "Category!=LiveBrain"`.`

Nenhuma regra `RN-*` muda. `bdd-para-testes` ("Comportamento comum a todos os cérebros → Suíte de contrato") continua correto.

## 3. Arquivos afetados

| Ação | Caminho | Responsabilidade |
|---|---|---|
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Contract/BrainContractHarness.cs` | Bancada abstrata e `SentToModel` (§2.1) |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Contract/ClaudeCodeContractHarness.cs` | Bancada do Claude Code (`FakeProcessRunner`) |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Contract/OllamaContractHarness.cs` | Bancada do Ollama (`FakeChatClient`) |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Contract/TutorBrainContractTests.cs` | Suíte abstrata: K1–K15 |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Contract/ClaudeCodeTutorBrainContractTests.cs`, `OllamaTutorBrainContractTests.cs` | Subclasses (só `CreateHarness`) |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Contract/ContractSuiteStructureTests.cs` | G1, G2 |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Contract/LiveBrainTraitTests.cs` | G3–G5 |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Contract/ClaudeCodeContractLiveTests.cs`, `OllamaContractLiveTests.cs` | L1, L2 (`LiveBrain`) |
| Criar | `tests/EnglishTutor.Infrastructure.Tests/Support/LiveContractChecks.cs` | Asserções comuns e os dois turnos do live |
| Alterar | `specs/sprints/SPRINT_1.md`, `ARCHITECTURE.md`, `.claude/skills/tdd-dotnet/SKILL.md` | Só pelo orquestrador, §2.6, antes da T1 |

Nenhum arquivo de `src/`. Nenhuma fixture nova (os fakes montam as respostas). `CHANGELOG` e `docs/us/US-008.md` são do Documentador.

## 4. Mapa cenário BDD → testes

Projeto: `EnglishTutor.Infrastructure.Tests`. Os K rodam **uma vez por subclasse** (`ClaudeCodeTutorBrainContractTests`, `OllamaTutorBrainContractTests`); "casos" é por subclasse. Textos únicos por teste (AP-017, AP-026): rótulos `t1`, `t2`…, resposta rejeitada ≠ aceita. Todos os K nascem verdes (caracterizam adaptadores prontos); o Red é por mutação (§5).

| Cenário | Classe | Método | Casos | Tipo |
|---|---|---|---|---|
| mesma suíte para todos os provedores | `TutorBrainContractTests` | K1 `Name_Always_IsTheCanonicalProviderName` | 1 | Contrato |
| mesma suíte | `TutorBrainContractTests` | K2 `SendAsync_WithEachOutputMode_ReturnsTheSameTutorReply` (Theory `JsonSchema`, `Tags`; mesma resposta lógica: `Reply t1?`, correção `orig t1`→`fixed t1` (`why t1`), pronúncia, vocabulário e elogio `… t1`; confere cada campo, `IsRawFallback` falso, `RawText` não vazio) | 2 | Contrato |
| mesma suíte | `TutorBrainContractTests` | K3 `SendAsync_WithReplyWithoutFeedback_ReturnsEmptyCorrectionsAndNullFeedbackFields` (Theory `JsonSchema` com `corrections: []` e nulos, `Tags` só com `<reply>`) | 2 | Contrato |
| mesma suíte | `TutorBrainContractTests` | K4a `SendAsync_WithOneOrTwoQuestions_ReturnsTheReply` (Theory 1 e 2 perguntas; limite de cima aceito, AP-016) | 2 | Contrato |
| mesma suíte | `TutorBrainContractTests` | K4b `SendAsync_WithZeroOrThreeQuestions_ThrowsInvalidOutputKeepingRawText` (Theory 0 e 3; `IsInvalidOutput`, `Rejection` com `Message` não vazia, `RawText` contém o rótulo único da resposta; a resposta vem como objeto JSON, então no Claude Code passa por `structured_output` e a asserção é `Contain`) | 2 | Contrato |
| mesma suíte | `TutorBrainContractTests` | K5 `SendAsync_WithUnusableOutput_ThrowsInvalidOutputWithExactRawText` (Theory: texto sem tags `plain words t1 without any tag`; `<reply>   </reply>` com `<feedback>` vazio; JSON truncado `{"reply": "Reply t3?"`; `RawText` **igual** ao texto, `Rejection` presente) | 3 | Contrato |
| mesma suíte (RN-BRAIN01) | `TutorBrainContractTests` | K6 `SendAsync_WithFullWindow_SendsSystemPromptAndEveryMessageInOrder` (sistema `system t1`; janela `student t1`, `tutor t1`, `student t2`, `tutor t2`, `student t3`; `SystemPrompt` começa por `system t1`; os 5 conteúdos aparecem em ordem crescente) | 1 | Contrato |
| mesma suíte (RN-BRAIN01, ADR-006) | `TutorBrainContractTests` | K7 `SendAsync_WithRetryMessages_SendsRejectedReplyAndCorrectionInOrder` (Theory: resposta rejeitada `rejected reply t1` e `""`; o aluno `student t1` e a correção `correction t1` chegam em ordem, a chamada responde; no caso `""` só os outros dois são conferidos) | 2 | Contrato |
| mesma suíte (RN-BRAIN01) | `TutorBrainContractTests` | K8 `SendAsync_OnSecondCallWithUnrelatedWindow_DoesNotCarryMessagesFromTheFirst` (`first window a1` e depois `second window b2 more words`; a 2ª `Conversation` não contém `a1`, contém `b2`; respostas `t1` e `t2` distintas) | 1 | Contrato |
| mesma suíte (RN-BRAIN01) | `TutorBrainContractTests` | K9 `SendAsync_WhenSecondRequestExtendsTheFirst_ReturnsTheSecondAnswer` (2 chamadas ao modelo; a 2ª resposta é `Reply t2?` e a 2ª `Conversation` contém `student t2`; o Claude Code pode mandar só ela, ADR-007) | 1 | Contrato |
| mesma suíte (RN-BRAIN09) | `TutorBrainContractTests` | K10 `SendAsync_WhenCallerCancels_PropagatesOperationCanceledException` (espera limitada; não é `BrainException`) | 1 | Contrato |
| mesma suíte (RN-BRAIN09) | `TutorBrainContractTests` | K11 `SendAsync_WhenModelDoesNotAnswerInTime_ThrowsTimeoutNamingTheSetting` (a 999 ms a chamada segue pendente; a 1000 ms lança `BrainException` `Timeout` cuja mensagem contém `Brain:<Provedor>:TimeoutSeconds`; os dois lados do limite, AP-016) | 1 | Contrato |
| mesma suíte (RN-BRAIN07) | `TutorBrainContractTests` | K12 `SendAsync_WhenProviderIsUnavailable_ThrowsBrainExceptionOfTheProviderKind` (tipo exato `BrainException`; `Kind` = `UnavailableKind`; mensagem contém `UnavailableSettingKey`) | 1 | Contrato |
| mesma suíte (RN-BRAIN07) | `TutorBrainContractTests` | K13 `SendAsync_AfterFailure_NextCallSucceeds` (inalcançável e depois resposta válida) | 1 | Contrato |
| mesma suíte | `TutorBrainContractTests` | K14 `SendAsync_WithNullRequest_ThrowsArgumentNullException` | 1 | Contrato |
| mesma suíte (RN-BRAIN04) | `TutorBrainContractTests` | K15 `SendAsync_ThroughConversationService_WhenFirstReplyIsInvalid_RetriesOnceAndReturnsSecondReply` (`ConversationService` real; 1ª resposta `plain words t1 without any tag`, 2ª válida `t2`; resposta `Reply t2?`, não crua; 2 chamadas ao modelo; a 2ª `Conversation` contém a instrução de correção com `format`) | 1 | Contrato |
| mesma suíte | `ContractSuiteStructureTests` | G1 `ContractSuite_ForEveryKnownProvider_HasOneConcreteSubclass` (Theory sobre `BrainProviders.All`: `<Provedor>TutorBrainContractTests` existe, não é abstrata e herda da base) | 2 | Unidade (reflexão) |
| mesma suíte | `ContractSuiteStructureTests` | G2 `ContractSuite_Always_DeclaresTestsOnlyInTheAbstractBase` (base abstrata com métodos de teste; subclasses não declaram `[Fact]`/`[Theory]`) | 1 | Unidade (reflexão) |
| testes live opcionais | `LiveBrainTraitTests` | G3 `LiveTests_Always_CarryTheLiveBrainTrait` (toda classe `*LiveTests` do assembly) | 1 | Unidade (reflexão) |
| testes live opcionais | `LiveBrainTraitTests` | G4 `LiveBrainTrait_OnlyOnClassesNamedLiveTests` (nenhuma outra classe tem o trait) | 1 | Unidade (reflexão) |
| testes live opcionais | `LiveBrainTraitTests` | G5 `LiveBrainTrait_Never_AppearsOnTheContractSuite` (base e subclasses de `TutorBrainContractTests` sem o trait) | 1 | Unidade (reflexão) |
| testes live opcionais | `ClaudeCodeContractLiveTests` | L1 `SendAsync_WithTwoTurnsThroughService_RepliesMeetTheContract` (`LiveBrain`; critério em §2.4) | 1 | Live |
| testes live opcionais | `OllamaContractLiveTests` | L2 `SendAsync_WithTwoTurnsThroughService_RepliesMeetTheContract` (`LiveBrain`) | 1 | Live |

Total por subclasse: K1 a K15 somam **23 casos** (1+2+2+2+2+3+1+2+1+1+1+1+1+1+1+1 = 23, em 16 métodos), **46** nas duas. G1–G5 somam 6 casos. L1 e L2 não entram na suíte rápida.

Cada comentário de teste cita a US e o nome exato do cenário: `// US-008 — Cenário: mesma suíte para todos os provedores — RN-BRAIN03` (K2–K5), `— RN-BRAIN01` (K6–K9), `— RN-BRAIN09` (K10, K11), `— RN-BRAIN07` (K12, K13), `— RN-BRAIN04` (K15); `// US-008 — Cenário: testes live opcionais` (G3–G5, L1, L2); K1, K14, G1 e G2: `— Cenário: mesma suíte para todos os provedores`.

**Condicionais da §2 (AP-047).** "Se o texto é objeto JSON e o modo é JsonSchema, vai por `structured_output`; senão por texto": caminho estruturado em K2 (JsonSchema) e K4b; caminho de texto em K2 (Tags) e nas três linhas de K5 (a do JSON truncado prova que um `{` que não é objeto válido cai no texto). "Instância nova recebe a janela inteira": K6, K7. "Pedido que estende o anterior": K9 (resultado, não texto). "Cada provedor tem o seu tipo para inalcançável": K12 nos dois (CliNotFound × Unknown). "Subclasses não declaram testes": G2. "Todo provedor tem subclasse": G1. "Live fora de `Category!=LiveBrain`": G3–G5 e D7. As duas fontes de falha dos adaptadores (processo × HTTP) passam pela mesma assertiva K12/K13 em papéis trocados (um provedor lança `Win32Exception`, o outro `HttpRequestException`).

**Placeholders (AP-054).** `Brain:<Provedor>:TimeoutSeconds` (K11) e `UnavailableSettingKey` (K12): um caso por provedor (as duas subclasses). `<Provedor>TutorBrainContractTests` (G1): um caso por provedor de `BrainProviders.All`. Sem números derivados nem cultura.

**Tolerância de verificação (AP-051).** Esta US não relaxa nenhuma validação. Os testes `LiveBrain` existentes que o filtro `Category=LiveBrain` executa e que tocam a regra de perguntas (`ClaudeCodeTutorBrainLiveTests`, `OllamaConversationLiveTests`) já foram ajustados na US-006 para 1..2; L1 e L2 não asseveram contagem de perguntas, só `TutorReplyValidator.Validate`.

## 5. Tarefas (ordem de execução TDD)

Esta US não escreve código de produção: todo o trabalho é código de teste. Não existe commit `feat`; cada tarefa é um commit `test(brain): ...` (e `refactor(brain): ...` se houver). O "Green" é a suíte passando. Todos os testes caracterizam adaptadores já prontos e **nascem verdes**; cada tarefa prescreve a mutação temporária que os faz falhar pelo efeito (AP-017, AP-029). A mutação é aplicada **depois** do commit do Green, em `src/`, e desfeita pela edição inversa, nunca por `git checkout`/`git restore` (AP-037). Antes de cada commit, o Executor preenche `Escolhas de forma` (AP-055: qualquer decisão de forma da bancada vai à §12 num commit `docs(ai)` **anterior** ao commit da tarefa). A guarda de `src/` intocado é o D12.

- [ ] **T1 — Bancada e suíte para o Claude Code (K1–K5)**
  Esqueleto: `BrainContractHarness`, `SentToModel`, `ClaudeCodeContractHarness`, `TutorBrainContractTests` com K1–K5, `ClaudeCodeTutorBrainContractTests`. Os membros ainda não usados da bancada (`Sent`, `Advance`, `CallStarted`) lançam `NotImplementedException` até a T2/T3. Nascem verdes: K1–K5 (12 casos no Claude Code). Mutações depois do Green, no `ClaudeCodeTutorBrain` ou no `TutorReplyValidator`: K1 — `Name` devolve outro texto; K2 — o adaptador devolve `parsed.Reply with { Corrections = [] }` (falha nos dois modos); K3 — o adaptador devolve `parsed.Reply with { Praise = "x" }`; K4a — `_maxQuestions` passa a 1; K4b — `_maxQuestions` passa a 3 e `_minQuestions` a 0; K5 — o adaptador lança `InvalidOutput` com `RawText` vazio.
  Commit: `test(brain): add the contract suite for common reply handling (Claude Code)`.
  Escolhas de forma: nenhuma

- [ ] **T2 — Janela completa e sem estado (K6–K9)**
  Esqueleto: implementa `Sent` e `Advance` no `ClaudeCodeContractHarness`; K6–K9 em `TutorBrainContractTests`. Nascem verdes: K6, K7 (2 casos), K8, K9 (5 casos). Mutações: K6 e K7 — o adaptador envia só a última mensagem mesmo sem sessão (`standardInput = request.Messages[^1].Content`); K8 — o adaptador acrescenta ao texto enviado as mensagens da chamada anterior; K9 — a 2ª chamada devolve a resposta da 1ª.
  Commit: `test(brain): cover full-window and stateless calls in the contract suite`.
  Escolhas de forma: nenhuma

- [ ] **T3 — Falhas, cancelamento e retentativa (K10–K15)**
  Esqueleto: implementa `CallStarted`, `EnqueueHang`, `EnqueueUnavailable` no `ClaudeCodeContractHarness`; K10–K15. Usa espera limitada (`WaitAsync(10 s)`) em todo teste que depende de cancelamento (AP-046). Nascem verdes: K10–K15 (6 casos). Mutações: K10 — remover `&& !ct.IsCancellationRequested` do `catch` do Timeout; K11 — multiplicar `TimeoutSeconds` por 2; K12 — o `catch (Win32Exception)` deixa de existir; K13 — o adaptador guarda a falha e a relança na chamada seguinte; K14 — remover `ArgumentNullException.ThrowIfNull`; K15 — o adaptador lança `Unknown` no lugar de `InvalidOutput`.
  Commit: `test(brain): cover timeout, unavailable, cancellation and retry in the contract suite`.
  Escolhas de forma: nenhuma

- [ ] **T4 — Subclasse do Ollama e guardas da suíte (G1, G2)**
  Red: primeiro `ContractSuiteStructureTests` (G1, G2). Falha: **G1 para `Ollama`** (não existe `OllamaTutorBrainContractTests`). Nascem verdes: G1 para `ClaudeCode` e G2 (mutação do G2: declarar um `[Fact]` em `ClaudeCodeTutorBrainContractTests`).
  Green: `OllamaContractHarness` e `OllamaTutorBrainContractTests`; os 23 casos do Ollama nascem verdes. Mutações equivalentes às das T1–T3, aplicadas ao `OllamaTutorBrain`: K1 `Name`; K2 perde as correções; K3 acrescenta `Praise = "x"`; K4a `_maxQuestions` do validador a 1; K4b `_maxQuestions` a 3 e `_minQuestions` a 0; K5 `RawText` vazio; K6/K7 `Skip(1)` na linha `messages.AddRange(request.Messages.Select(ToChatMessage));` de `OllamaTutorBrain.SendAsync` (passa a `request.Messages.Skip(1).Select(...)`); K8 histórico acumulado em campo; K9 a 2ª chamada devolve a resposta da 1ª; K10 sem `&& !ct.IsCancellationRequested` no `catch` do Timeout; K11 `TimeoutSeconds` ×2; K12 `IsServerFailure` devolve falso (a `HttpRequestException` vaza); K13 o adaptador guarda a falha e a relança na chamada seguinte; K14 remover `ArgumentNullException.ThrowIfNull(request)`; K15 `Unknown` no lugar de `InvalidOutput`.
  Commit: dois commits em ordem: `test(brain): add contract suite structure guards` (G1 vermelho para o Ollama) e `test(brain): run the contract suite for Ollama` (subclasse e bancada; G1 verde).
  Escolhas de forma: nenhuma

- [ ] **T5 — Guarda do trait `LiveBrain` e variante live (G3–G5, L1, L2)**
  Esqueleto: `LiveBrainTraitTests` (G3–G5). Nascem verdes todos (as classes `LiveTests` existentes já têm o trait). Mutações: G3 — remover o trait de `ProcessRunnerLiveTests`; G4 — acrescentar o trait a uma classe de teste comum sem o sufixo (ex.: `TutorReplyParserTests`); G5 — acrescentar o trait a `ClaudeCodeTutorBrainContractTests`.
  Green: `LiveContractChecks`, `ClaudeCodeContractLiveTests`, `OllamaContractLiveTests` (L1 e L2 não rodam na suíte rápida; G3 passa a cobri-las e continua verde). O Executor roda o D8 (3 rodadas) e o D7 (controles positivo e negativo).
  Commit: `test(brain): guard the LiveBrain trait` e `test(brain): add live conformance tests for both providers`.
  Escolhas de forma: nenhuma

## 6. Pacotes NuGet

Nenhum pacote novo. (`Microsoft.Extensions.TimeProvider.Testing`, `Microsoft.Extensions.DependencyInjection`, FluentAssertions, xUnit v3 e o `OllamaApiClient` já estão no projeto de teste ou na Infrastructure.)

## 7. Riscos e mitigação

| Risco | Mitigação |
|---|---|
| **Oscilação do `LiveBrain` (AP-032).** L1/L2 têm tolerância zero de resposta crua em 2 turnos; o Ollama 14B errou 0 a 2 frases de 5 sem retentativa na US-006, e com a retentativa passou 5/5 em 3 rodadas | Critério medido na camada `ConversationService` com a retentativa; passar em 3 rodadas seguidas (D8). Se ficar vermelho depois, **quem decide é o usuário**, com registro prévio na spec (aumentar tolerância, trocar o modelo padrão ou mover a asserção); o Executor não relaxa por conta própria |
| **L1 (limite aceito): a retentativa não é exigida nos testes live.** A taxa de retentativas usadas não é medida aqui | Consumidores: validador (nenhum efeito); `LiveBrain` (L1/L2 passam com ou sem retentativa); DoD (D8 idem). A prova do mecanismo é K15 (fakes). A taxa fica para a US-009 (`eval`), que já mede formato e latência; sem registro novo na spec dela |
| **L2 (limite aceito): o contrato não cobre `NotAuthenticated`, `UsageLimitReached`, argumentos do processo, `format` do Ollama** | Consumidores: cada um continua nos testes do adaptador (US-004, US-005, US-006); `FallbackTutorBrain` só olha o `Kind` (US-007); o contrato muda só se o `ITutorBrain` mudar |
| **L3 (limite aceito): K12 usa tipos diferentes por provedor** (Claude Code `CliNotFound`, Ollama `Unknown`) | Reflete o ADR-009 e o limite L3 da US-007. Se uma US futura criar `ProviderUnavailable`, a propriedade `UnavailableKind` da bancada muda num ponto só |
| `Sent.Conversation` do Claude Code com `--resume` manda só a última mensagem | K6/K7 usam instância nova; K9 confere resultado; K8 usa janelas sem relação (§2.3) |
| O `RawText` do Claude Code em `structured_output` pode diferir do texto enviado (re-serialização) | K4b usa `Contain` com rótulo único; só K5 usa igualdade, e com textos que seguem pelo caminho de texto |
| `dotnet test --list-tests` pode não imprimir o nome completo da classe | O D7 é a contagem de linhas com `ContractLiveTests`, subcadeia que só existe nos nomes das duas classes live: nenhum método planejado na §4 (K1–K15, G1–G5, L1, L2) a contém, então as guardas G3/G4 (que rodam em `Category!=LiveBrain`) não a casam; o controle negativo é o primeiro filtro (0) e o positivo, o segundo (≥ 2). Se o formato não mostrar a classe, o Executor registra na §11 e usa o resumo do `--logger "console;verbosity=normal"` como controle, com um controle positivo (`Category=LiveBrain` lista os testes live) |
| O D5 da spec filtra por `ContractTests` e arrastaria testes live | Nomes `*ContractLiveTests` e `ContractSuiteStructureTests` não contêm a subcadeia `ContractTests` (§2.4) |
| Hook de edição acusa símbolo inexistente ao criar o par bancada/suíte (AP-044) | Criar primeiro o arquivo que declara e depois o que usa; se o hook falhar logo após a primeira edição, editar o outro arquivo antes de investigar |

## 8. Fora de escopo

- Contrato sobre o `FallbackTutorBrain`, que não é provedor (US-007 §8); `ClaudeCodeStreamingBrain` (não existe).
- Classificação de falhas específicas do Claude Code (`NotAuthenticated`, `UsageLimitReached`) no contrato: continua nos testes do adaptador (US-005).
- Medir a taxa de retentativa ou de correções esperadas nos testes live: é da US-009 (`eval`). Nada novo a registrar na spec dela: ela já mede formato e latência, e correções esperadas já são o critério D6 dela.
- Novas fixtures gravadas: a suíte monta as respostas por código, então nada para regravar (AP-048 não se aplica).

Adiamentos procurados nas P*, nos comentários de teste e nos ADRs (AP-040): os pontos "a suíte de contrato (US-008) confere" são fechados por esta US: ADR-005 (adaptador usa o parser: K4a, K4b, K5), ADR-006 (tradução em `InvalidOutput`: K5, K15), ADR-007 (`FakeProcessRunner` reaproveitado: bancada do Claude Code), ADR-009 (`FakeChatClient` reaproveitado: bancada do Ollama). Nenhum adiamento novo para outra US.

## 9. Perguntas em aberto

- **P1 — Camada e critério do `LiveBrain`.** Resposta padrão: `ConversationService` real sobre o adaptador real, 2 turnos, zero respostas cruas após a retentativa única, 3 rodadas seguidas; a retentativa não é exigida (§2.4, L1). Teste: `ClaudeCodeContractLiveTests.SendAsync_WithTwoTurnsThroughService_RepliesMeetTheContract` e `OllamaContractLiveTests.SendAsync_WithTwoTurnsThroughService_RepliesMeetTheContract`.
- **P2 — Variante live separada, não subclasses da suíte.** Resposta padrão: classes à parte, porque os fakes simulam Timeout, resposta inválida e inalcançável que o modelo real não produz sob demanda; a ARCHITECTURE §10 e a skill são ajustadas (§2.6). Teste: `LiveBrainTraitTests.LiveBrainTrait_Never_AppearsOnTheContractSuite`.
- **P3 — Subclasses sem testes próprios.** Resposta padrão: sim; a suíte é uma só e um teste específico de provedor mora no teste do adaptador. Teste: `ContractSuiteStructureTests.ContractSuite_Always_DeclaresTestsOnlyInTheAbstractBase`.
- **P4 — Falhas no contrato.** Resposta padrão: só Timeout e inalcançável, com o `Kind` próprio de cada provedor; sem conferir o texto inteiro da mensagem (§2.2). Teste: `TutorBrainContractTests.SendAsync_WhenProviderIsUnavailable_ThrowsBrainExceptionOfTheProviderKind`.
- **P5 — O `FallbackTutorBrain` fica fora.** Resposta padrão: sim (decisão da US-007 §8); a suíte cobre os provedores de `BrainProviders.All`. Teste: `ContractSuiteStructureTests.ContractSuite_ForEveryKnownProvider_HasOneConcreteSubclass`.
- **P6 — Resume do Claude Code e o contrato de janela.** Resposta padrão: janela completa provada com instância nova; pedido que estende o anterior prova só o resultado (§2.3). Teste: `TutorBrainContractTests.SendAsync_WhenSecondRequestExtendsTheFirst_ReturnsTheSecondAnswer`.
- **P7 — Tags e JSON no contrato.** Resposta padrão: os dois modos de saída entram em K2 e K3; os demais usam `JsonSchema` (o parser é único e já tem testes próprios). Teste: `TutorBrainContractTests.SendAsync_WithEachOutputMode_ReturnsTheSameTutorReply`.
- **P8 — Formato de `--list-tests` na DoD D7.** Resposta padrão: contagem de linhas com `ContractLiveTests` (B1); se a saída não trouxer a classe, ver riscos. Sem efeito observável (só o comando da DoD).

## 10. Definition of Done (comandos verificáveis)

| # | Comando | Esperado |
|---|---|---|
| D1 | `dotnet build` | 0 warnings, 0 erros |
| D2 | `dotnet test --filter "Category!=LiveBrain"` | todos verdes, em < 30 s |
| D3 | `dotnet test --filter "Category!=LiveBrain" --collect:"XPlat Code Coverage"` | Core ≥ 80% de linhas; o Executor registra o line-rate **mesclado** do assembly `EnglishTutor.Core` (`reportgenerator` sobre os três `coverage.cobertura.xml`, AP-056); como `src/` não muda, o número não deve cair em relação à US-007 |
| D4 | `dotnet format --verify-no-changes` | sem diferenças |
| D5 | `dotnet test --filter "FullyQualifiedName~ContractTests" --logger "console;verbosity=normal"` (da spec) | os mesmos 16 testes do contrato (23 casos) listados para `ClaudeCodeTutorBrainContractTests` e para `OllamaTutorBrainContractTests`, todos verdes (46 casos) |
| D6 | `dotnet test --filter "FullyQualifiedName~ClaudeCodeTutorBrainContractTests"` e o mesmo com `OllamaTutorBrainContractTests` | verde, 23 testes passados em cada um |
| D7 | (Git Bash) `dotnet test --no-build --filter "Category!=LiveBrain" --list-tests \| grep -c "ContractLiveTests"` e `dotnet test --no-build --filter "Category=LiveBrain" --list-tests \| grep -c "ContractLiveTests"` | `0` no primeiro (código de saída 1 com contagem 0 é o esperado); pelo menos `2` no segundo: os testes live não entram em `Category!=LiveBrain` e aparecem em `Category=LiveBrain` |
| D8 | `dotnet test --filter "Category=LiveBrain&FullyQualifiedName~ContractLiveTests"`, no ambiente do usuário, com a CLI `claude` autenticada e o Ollama rodando com `qwen2.5:14b-instruct-q4_K_M` baixado | verde (2 testes) em **3 rodadas seguidas**; critério e tolerância em §2.4; sem Ollama ou sem a CLI no ambiente, o Executor registra "não reproduzido" na §11 e o usuário decide |
| D9 | `dotnet test --filter "FullyQualifiedName~ContractSuiteStructureTests\|FullyQualifiedName~LiveBrainTraitTests"` | verde (G1–G5, 6 casos) |
| D10 | `git grep -n -F "Provisional selector" -- src tests` | nenhuma linha (continua valendo da US-007; código de saída 1) |
| D11 | (Git Bash) `git diff develop...HEAD \| LC_ALL=C grep -c -e $'\xef\xbb\xbf' -e $'\xe2\x80\x8b' -e $'\xc2\xa0'`, com todo o código da US commitado | imprime `0` (código de saída 1 com contagem 0 é o esperado) |
| D12 | `git diff --name-only develop...HEAD -- src` | nenhuma linha (esta US não altera produção; as mutações foram desfeitas) |
| D13 | `git status --short` depois de D2–D9 | nenhum arquivo de `TestResults/`, `coverage/`, `bin/`, `obj/` ou `logs/` |

Notas: a US não mexe no console, então não há comando com pipe no Windows PowerShell 5.1 (AP-006 não se aplica). Critério `LiveBrain` do D8: camada `ConversationService` com retentativa única sobre o adaptador real, tolerância zero de resposta crua, 3 rodadas; o risco de oscilação e o dono da decisão estão na §7.

## 11. Evidências
_Preenchido pelo Executor: saída resumida de cada comando da DoD, com data e hash do commit._

## 12. Comentários do usuário
_O usuário escreve aqui ajustes ao plano. O Planejador responde cada um abaixo dele._

### 2026-10-22 — pedido do Revisor do plano (rodada 1)
- **B1** — O comando do D7 (`--list-tests | grep -c "LiveTests"`) não dá `0`: os métodos G3 `LiveTests_Always_CarryTheLiveBrainTrait` e G4 `LiveBrainTrait_OnlyOnClassesNamedLiveTests` rodam em `Category!=LiveBrain` e contêm "LiveTests" no nome. Ajustar o D7 (no plano e no texto idêntico a registrar em `SPRINT_1.md`) para casar só o que identifica os testes live (ex.: nome de classe `...LiveTests.` ou `ContractLiveTests` nos dois filtros) ou renomear G3/G4; mostrar controle positivo e negativo (AP-035).
- Sugestões a avaliar: S1 (mutações K13/K14 também no Ollama em T4), S2 (K13 citar `RN-BRAIN07`), S3 (apontar o ponto exato da mutação `Skip(1)` em `OllamaTutorBrain`).

Respostas do Planejador (rodada 1):
- **B1** → Incorporado em §2.6 (Commit 1, D7), §7 (linha do `--list-tests`), §9 P8 e §10 D7. O padrão passou a ser `ContractLiveTests` nos dois filtros. Essa subcadeia só existe nos nomes das duas classes live, e nenhum método da §4 a contém (G3 `LiveTests_Always_CarryTheLiveBrainTrait` e G4 `LiveBrainTrait_OnlyOnClassesNamedLiveTests` não casam). Controle negativo = primeiro filtro (0); positivo = segundo (≥ 2). O texto do D7 é idêntico na §2.6 e na §10. G3/G4 não foram renomeados.
- **S1** → §5 T4: acrescentadas as mutações K3, K4a/K4b, K9, K13 e K14 para o Ollama.
- **S2** → §4 (linha de K13 e linha de comentários): K13 passa a citar `RN-BRAIN07`.
- **S3** → §5 T4: a mutação `Skip(1)` aponta a linha `messages.AddRange(request.Messages.Select(ToChatMessage));` de `OllamaTutorBrain.SendAsync` (conferida na linha 54 de `OllamaTutorBrain.cs`).

### 2026-10-23 — aprovação do usuário
- Plano aprovado. Perguntas em aberto P1 a P8: respostas padrão aceitas, sem alteração.
- S4 (Revisor, rodada 2) não incorporada, por decisão do usuário.
- Nenhum ADR proposto, então nenhum status a alterar.
- Alterações em outros documentos (§2.6) aplicadas em `docs(ai): update spec for US-008` e `docs(ai): update architecture for US-008`.

### 2026-10-23 — decisões do Executor (forma, AP-009/AP-010)
- **K10 (desvio de técnica, AP-010).** A mutação prescrita na T3 (remover `&& !ct.IsCancellationRequested` do `catch` do Timeout) não falhou no Claude Code: com o cancelamento sozinho o `timeout` nunca dispara, então a condição já era falsa e o K10 original não exercita o risco. Técnica escolhida: `EnqueueHang(Action? onCancelled = null)` na bancada, repassando ao `HangsUntilCancelled(onCancelled)` do fake (que roda depois do cancelamento e antes de a exceção sair, ordem determinística); o K10 passa `() => harness.Advance(1 s)`, de modo que, quando o adaptador avalia o `catch`, o cancelamento do chamador **e** o do tempo já ocorreram. Esperado: `OperationCanceledException` (o chamador vence). Com a mutação, vira `BrainException` `Timeout` e o K10 falha. Vale para os dois provedores (a bancada do Ollama implementa o mesmo parâmetro).
