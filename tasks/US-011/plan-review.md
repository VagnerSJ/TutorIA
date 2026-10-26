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
