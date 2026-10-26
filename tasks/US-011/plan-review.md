# Revisão do plano — US-011 O professor fala o `reply` (Piper)

Data: 2026-10-26
Escopo revisado: `tasks/US-011/plan.md` (commit eb6d9f4, que também traz o ADR-012 Proposto), US-011 em `specs/sprints/SPRINT_2.md`, `ARCHITECTURE.md`, `CLAUDE.md` e as skills `padroes-codigo`, `tdd-dotnet`, `bdd-para-testes`. Não rodei build nem testes, porque ainda não há código.
Resultado: REPROVADO (1 bloqueante)

## Verificações (seção "Revisão de plano" do checklist)
| Item | Resultado |
|---|---|
| [O] Todo cenário BDD no mapa da §4, com projeto, classe e método `Metodo_Cenario_Resultado` | ✅ Os 6 cenários estão mapeados. |
| [O] Resposta P* com efeito observável tem teste na §4 e tarefa na §5 | ✅ P1 a P7 têm teste; P8 não tem efeito observável. |
| [O] DoD da §10 cobre a DoD da US (D5 a D8), com comando e resultado esperado, e inclui cobertura do Core ≥ 80% | ❌ A D5 copiada da spec roda os testes `LiveVoice` (ver B1). A D3 cobre a cobertura. |
| [O] Conformidade com R1–R4, `RN-*` e regras rápidas; decisão técnica com ADR Proposto | ✅ `ReplySpeaker` fica no Core (R4), o adaptador implementa uma porta (R3), a ADR-012 está commitada com o plano. |
| [O] Escopo: nada além nem aquém; adiamentos com texto exato (AP-022); limites aceitos com consumidores (AP-040) | ✅ O texto exato para a US-012 (D7) está na §2.6. Os 3 limites aceitos da §7 listam consumidores e efeito. |
| [O] Consistência interna §3 × §4 × §5 × §6; nomes da §5 idênticos aos da §4 (AP-025, AP-046) | ✅ Conferi os nomes por amostragem e batem. |
| Mensagens com transformações testadas nos dois lados do limite (AP-016) e trecho próprio do projeto (AP-015) | ✅ O achatamento de linhas tem os dois lados. Sugestão S1 abaixo. |
| Afirmações condicionais da §2 com teste (AP-047) | ✅ Os testes de `PiperVoiceOutputCheck` cobrem arquivo a arquivo, com os presentes ausentes da mensagem. |
| [O] Ordem das tarefas preserva o Red | ✅ Os testes que nascem verdes estão listados, cada um com a mutação que prova o Red. |
| Relaxamento de validação por tolerância (AP-051) | N/A |
| Renomear ou remover cenário ou texto (AP-050) | ✅ O inventário foi feito por Grep e só a spec cita os cenários. |
| Critério `LiveBrain` e `LiveVoice` (AP-032, AP-052) | ✅ A camada medida, a tolerância 0 e quem decide estão no fim da §10. |
| `ValidateOnStart` em mais de uma classe de Options (AP-039) | ✅ `RunAsync_WhenPiperAndTtsAreBothInvalid_ShowsBothFailures`. |
| Justificativas que citam outra US (AP-036) | ✅ O papel da US-012 confere com a spec. |
| Busca de "nenhuma linha" na DoD (AP-035) | ✅ A D13 (`git ls-files`) é uma verificação nova, não uma busca de texto antigo. |
| Alteração na `ARCHITECTURE.md` lista também o `CLAUDE.md` e as skills | ✅ Conferi as 6 linhas antigas citadas na §2.5 no `ARCHITECTURE.md` (linhas 14, 57, 72, 150, 254 e 256) e o texto bate. A sincronia com `CLAUDE.md` e skills está declarada. |
| Riscos técnicos e viabilidade no SDK | ✅ O risco do `NAudio.WinMM` no `net10.0` tem mitigação (§7) e `FakeTimeProvider` já é referenciado nos 3 projetos de teste. |

## Bloqueantes
- [ ] **B1** — `tasks/US-011/plan.md`, §10, linha D5 (`dotnet test --filter "FullyQualifiedName~Tts"`) com a §3 (`tests/EnglishTutor.Infrastructure.Tests/Tts/PiperLiveVoiceTests.cs`). Os testes seguem o namespace da pasta (por exemplo `EnglishTutor.Infrastructure.Tests.Brain` em `ClaudeCodeTutorBrainLiveTests.cs`). Logo `PiperLiveVoiceTests` terá `Tts` no nome completo e o filtro da D5 vai rodá-lo, com `Category=LiveVoice`, exigindo Piper real e alto-falante. A D5 promete "nenhum executável real", então ela falha onde não há Piper ou áudio (inclusive no CI). Esperado: a D5 executa só os testes unitários do adaptador, sem tocar os `LiveVoice` (checklist, DoD verificável com resultado esperado). Ajuste o filtro da D5 e, como a D5 também está na spec da US, registre a mudança em §2.6. O resultado esperado também precisa dizer que a D5 não executa nenhum teste `LiveVoice` (o `--list-tests` pode provar isso).

## Sugestões
- **S1** — §4, `PiperTextToSpeechTests.SynthesizeAsync_WhenTimeoutElapses_ThrowsTimeoutAndCancelsRunnerToken`. O teste só cobre o lado "estourou". Falta o lado "ainda dentro do tempo" (avançar o relógio menos que `TimeoutSeconds` e a síntese terminar normalmente), para a transformação "estouro → `TimeoutException`" ter os dois lados (AP-016).
- **S2** — §2.3 e §7. `AddEnglishTutorInfrastructure_Always_RegistersPiperAdaptersAndValidator` resolve `IAudioPlayer`, ou seja, constrói o `NAudioPlayer` também no CI em `ubuntu-latest`. O plano devia dizer que o construtor não abre o dispositivo (só o `PlayAsync`), para o teste não depender de WinMM.

## Pontos positivos
- Cada teste que nasce verde tem um Red por mutação explícito, e isso vale também para os testes de cancelamento e de timeout (técnica do chamador cancelado e `FakeTimeProvider`).
- ADR-012 e as mudanças de `ARCHITECTURE.md` e de spec vêm antes da T1, com o texto antigo citado.
- Critério de saída por canal (AP-057) e testes de cultura pt-BR para `length_scale` (AP-054).
- Limites aceitos listam consumidores (AP-040) e as 3 mudanças de cenário da spec estão no texto exato.

## Aprendizados para o processo
- Filtro de DoD copiado da spec por nome de namespace (`~Tts`) pega testes `Live*` da mesma pasta. Sugestão de regra para o Planejador: todo filtro `FullyQualifiedName~X` na DoD deve ser conferido contra a lista de classes `Live*` da pasta e levar `&Category!=LiveBrain&Category!=LiveVoice`, a menos que o objetivo seja justamente o live.

---

## Rodada 2

Data: 2026-10-26
Escopo revisado: `tasks/US-011/plan.md` no commit 2fd67df (revisão do plano da rodada 1, com o `plan-review.md` do d4c9dad). Li também o `plan-review.md` da rodada 1 e fiz Grep no repositório. Não rodei build nem testes: ainda não há código.
Resultado: APROVADO (0 bloqueantes)

### Itens da rodada anterior
- **B1 (filtro da D5): resolvido no plano.**
  - §2.6 item 4 cita o texto antigo da linha inteira e dá o novo. O filtro passa a ser `FullyQualifiedName~Tts&Category!=LiveBrain&Category!=LiveVoice`, e o esperado diz que nenhum teste `LiveVoice` roda.
  - §10 D5 usa o mesmo filtro, mais `--list-tests`, e exige que a lista não contenha `PiperLiveVoiceTests`.
  - A T7 prova os dois lados, com o filtro antigo listando a classe e o novo não. A §7 ganhou o risco correspondente.
  - Conferi no Grep que a D5 antiga existe só em `specs/sprints/SPRINT_2.md:140` (as outras ocorrências estão em `tasks/`). O texto antigo da §2.6 bate com essa linha.
  - A D14 (`grep -F 'filter "FullyQualifiedName~Tts"' specs`) acha a linha antiga hoje e não acha a nova, que tem `~Tts&Category...`. Isso dá o lado "achou antes" e o "não acha depois" (AP-035, AP-050).
  - Também conferi que a D9 não casa com classes `LiveVoice`.
- **S1 (lado "dentro do tempo" do timeout): resolvido.** `SynthesizeAsync_WhenFinishesBeforeTimeout_ReturnsClipAndDoesNotCancelRunnerToken` está na §4 e na T5, com a mutação do prazo fixo de 10 s como Red.
- **S2 (construtor do `NAudioPlayer` sem efeito): resolvido.** Está declarado na §2.3, na §3, na §4 (teste de registros) e na T7.

### Verificações (seção "Revisão de plano" do checklist)
| Item | Resultado |
|---|---|
| [O] Cenários BDD no mapa da §4 (projeto, classe, método) | ✅ |
| [O] Respostas P* com efeito observável têm teste e tarefa | ✅ |
| [O] DoD da §10 cobre a DoD da US, com comando e resultado esperado (cobertura do Core na D3) | ✅ (D5 corrigida) |
| [O] R1–R4, `RN-*`, regras rápidas e ADR-012 Proposto | ✅ |
| [O] Escopo e adiamentos com texto exato (US-012 D7, AP-022); limites aceitos com consumidores (AP-040) | ✅ |
| [O] Consistência §3 × §4 × §5 × §6 | ✅ |
| Transformações testadas nos dois lados (AP-016); asserção com texto do projeto (AP-015) | ✅ |
| Condicionais da §2 com teste (AP-047) | ✅ |
| [O] Ordem das tarefas preserva o Red; "nasce verde" com mutação | ✅ |
| `ValidateOnStart` com duas seções inválidas (AP-039) | ✅ |
| Inventário de renomeações (AP-050) e "nenhuma linha" com os dois lados (AP-035) | ✅ |
| Critério `LiveVoice` (AP-032, AP-052) | ✅ |
| `ARCHITECTURE.md`, `CLAUDE.md` e skills em sincronia | ✅ |
| Riscos e viabilidade (`NAudio.WinMM` no `net10.0`) | ✅ |

### Bloqueantes
Nenhum.

### Sugestões
- **S3** — §10, D14. O comando só vale como prova se o Executor o rodar no `develop` antes do commit de spec, como o plano diz. Se a ordem dos commits mudar, a "contagem antes" some. Só registrar essa contagem na §11.

### Pontos positivos
- A correção do B1 ficou completa: spec, DoD, tarefa de prova, risco e uma conferência das outras linhas da DoD.
- Os dois lados do filtro são demonstrados por `--list-tests`, e a D14 prova que o filtro antigo não sobra na spec.
- A S1 e a S2 da rodada 1 foram incorporadas com mutação de Red e com referência cruzada entre §2, §3, §4 e §5.

### Aprendizados para o processo
- Mesmo registro da rodada 1: filtro de DoD por nome de namespace (`~X`) deve levar `&Category!=LiveBrain&Category!=LiveVoice`, a menos que o objetivo seja o live. Aqui o Planejador aplicou a regra e provou com `--list-tests`.
