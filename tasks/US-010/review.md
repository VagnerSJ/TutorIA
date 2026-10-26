# Revisão — US-010 Portas de voz e áudio no Core, com configuração validada

Data: 2026-10-26
Commits revisados: 714b803..6c526be (develop..HEAD, 22 commits, incluindo os de docs/plano)
Resultado: APROVADO (0 bloqueantes)

## Verificações
| Item | Resultado |
|---|---|
| Build sem warnings | ✅ 0 avisos, 0 erros (`--no-incremental`) |
| Testes (exceto LiveBrain e LiveVoice) | ✅ 672 passaram (Core 306, Infra 261, Console 105), 0 falhos, 0 ignorados, cada projeto < 1 s |
| Cobertura de linhas do Core | 98,4% (mesclada via reportgenerator; total da solução 91,9%; `AudioClip` 100%) |
| Cenários BDD com teste | 4/4 (portas existem; opções inválidas; valores padrão; clipe inválido), conferidos com a §4 do plano e o código |
| Teste antes do código (ordem de commits) | ✅ cada `test(...)` precede o `feat(...)` (714b803→438979f, bd06636→bade6c1, 15e43e3→19d3a57, 9b56c3d→548f852, a2a9e12→8cf7f8c) |
| Evidências da DoD conferem | ✅ D1, D2, D3, D4, D8 (nenhuma linha no working tree) e D10 (contagem 0) batem com o que obtive. D6 e D7 (`dotnet run`): evidência do Executor, não reproduzida; coerente com o código (bind de Tts/Stt/Voice com `ValidateOnStart` no `BuildHost`, código 2) |
| Intenção antes do código (spec/ADR → código) | ✅ plano aprovado (3774623) e `ARCHITECTURE.md` (7e4d38f) antes do primeiro teste; sem ADR (nenhuma decisão nova, plano §2); spec sem mudança |

Desvios declarados pelo Executor, verificados em `AudioClipTests.cs` e `VoicePortsTests.cs`:
- `AudioClipTests`: o `[Theory]` de "sem amostras" virou `Constructor_WhenThereAreNoSamples_ThrowsWithExpectation` (Fact, array vazio) mais `Constructor_WhenSamplesAreDefault_ThrowsWithExpectation` (Fact, `default`). Os dois mantêm a asserção `must not be empty`. A linha de 1 amostra saiu do Theory e virou `Constructor_WithSingleSample_IsValid` (Fact). A cobertura de comportamento é a mesma do plano; é só formato.
- `VoicePortsTests`: usa nomes completos em string e `_core.GetType(...)` no lugar de `typeof`, com `NotBeNull`, `IsPublic`, `IsInterface` e `IsSealed`. A verificação é equivalente; é só formato e mantém o Red por comportamento.

## Itens da rodada anterior
N/A (primeira revisão de código).

## Bloqueantes
Nenhum.

## Sugestões
- **S1** — `tasks/US-010/plan.md` §4 e §5 T1 — os nomes dos 2 testes de `AudioClipTests` ficaram diferentes do mapa (e `VoicePortsTests` usa strings). O desvio foi registrado só em §11, depois da implementação. Sugiro alinhar §4 aos nomes reais na etapa de documentação. Não é bloqueante: são nomes de teste, não texto exibido, exceção ou nome público (AP-014/027/055).
- **S2** — `tests/EnglishTutor.Core.Tests/Audio/AudioClipTests.cs` — `Constructor_WithSingleSample_IsValid` não tem o formato `Metodo_Cenario_Resultado` completo no sentido estrito. Está aceitável.

## Pontos positivos
- Core sem dependências novas (R1); a validação de faixa está no Core, não nos adaptadores (R4).
- Cobertura de 98,4% no Core.
- `appsettings.json` ganhou as seções com os padrões e tem teste de igualdade com os padrões das classes.
- Filtro `Category!=LiveBrain&Category!=LiveVoice` sincronizado em hooks, CI, README, agentes e skills; o D8 devolve nenhuma linha.
- Um commit `test` precede cada `feat`, e as mutações de Red dos testes que nascem verdes foram registradas.

## Aprendizados para o processo
- Desvio de nome de teste em relação ao mapa da §4 é registrado só no fim, em §11. Sugestão: o Executor atualizar a §4 no mesmo commit em que renomeia o teste, para o mapa continuar batendo com o código (AP-025).
