# Revisão do plano — US-007 Troca de cérebro por configuração e fallback

Data: 2026-10-22
Escopo revisado: `tasks/US-007/plan.md` (commit 0badc52), `docs/adr/ADR-010-cerebro-reserva-decorator-politica-no-core-e-aviso-por-porta.md` (Proposto, já versionado), SPRINT_1 §US-007, RN-BRAIN04/07/08, ARCHITECTURE (§3, §4, §6.1, §6.3, R1–R4), código atual de Core, Infrastructure e testes.
Resultado: REPROVADO (2 bloqueantes)

## Verificações (seção "Revisão de plano" do checklist)
| Item | Resultado |
|---|---|
| [O] Todo cenário BDD no mapa §4 (projeto, classe, método `Metodo_Cenario_Resultado`) | ✅ os 3 cenários da spec e os 3 novos da §2.7(b) têm teste |
| [O] Cada P* com efeito observável tem teste na §4 e tarefa na §5 | ✅ P1–P13 mapeados |
| [O] DoD §10 cobre a DoD da spec, com cobertura do Core ≥ 80% | ✅ D5–D7 da spec = D5–D7 do plano, mais D8/D9 propostos |
| [O] Conformidade com R1–R4, `RN-*`, CLAUDE.md; ADR Proposto | ✅ política no Core (R4), aviso por porta (R2), sem pacote novo no Core. ADR-010 está versionado como Proposto |
| [O] Escopo e adiamentos (AP-022, AP-040) | ✅ nada a registrar em outra US; limites L1–L4 listam consumidores |
| [O] Consistência interna §3 × §4 × §5 × §6 | ❌ B1 |
| Transformações com teste nos dois lados (AP-016); mensagens (AP-015) | ✅ C8/C9/C10, V3–V6 |
| Condicionais da §2 com teste (AP-047) | ✅ cobertas na §4; V3/V6/V5 cobrem as duas fontes e os papéis trocados |
| [O] Ordem das tarefas preserva o Red | ❌ B2 |
| Relaxamento de validação × `LiveBrain` (AP-051) | N/A |
| Renomeação com inventário nominal (AP-050) | ✅ o Grep achou só a linha 18 de `InfrastructureServiceCollectionExtensionsTests.cs`, e D12 prova os dois lados |
| Critério `LiveBrain` (AP-032, AP-052) | N/A (nenhum `LiveBrain` nesta US, declarado) |
| `ValidateOnStart` em várias Options (AP-039) | ✅ C7 ganha linha com `Brain:FallbackProvider`; teste de caixa diferente em B1/B3 |
| Justificativas citam cenário de outra US corretamente (AP-036) | ✅ o `Dado` do cenário de limite de uso confere com o SPRINT_1 |
| "Nenhuma linha" na DoD tem o outro lado (AP-035) | ✅ D7 (achei 1 ocorrência em `InfrastructureServiceCollectionExtensions.cs:33`) e D12 (achei a linha 18) |
| Alteração da ARCHITECTURE × CLAUDE.md/skills | ✅ textos antigos da §2.8 conferem com as linhas 55, 64, 72, 107, 184 e 216. O plano registra o Grep de sincronia |
| Riscos e ambiguidades do Executor | ✅ ver S1 e S2 |
| Viabilidade no SDK e nas bibliotecas | ✅ `TryAddKeyedSingleton` e `GetRequiredKeyedService` existem na abstração já referenciada. EventId 2003 está livre (usados: 2000, 2001, 2002). `Name` dos cérebros são os de `BrainProviders` |

## Bloqueantes
- [ ] **B1** — `tasks/US-007/plan.md` §4 (`BrainFactoryTests`) × §3, §5 T4 e D5 — a §3 e a T4 prometem "B1–B6", mas a §4 só tem B1, B2, B3, B4 e B6. Não existe B5, e a contagem não bate (AP-025). Esperado: ou o plano define o B5 com projeto, classe, método e o cenário que ele prova, ou renumera para B1–B5 em todo o plano (§3, §4, §5 T4, D5) para que a conferência por contagem seja confiável.
- [ ] **B2** — `tasks/US-007/plan.md` §5 T4 / §4 DI3 (`AddEnglishTutorInfrastructure_WhenNotifierIsAlreadyRegistered_KeepsIt`) — o plano diz que o DI3 falha no Red da T4. Pelo que está descrito, o DI3 registra um notificador próprio, chama `AddEnglishTutorInfrastructure` e resolve `IBrainSwitchNotifier`. Com a T2 feita, esse teste já resolve o registrado e **nasce verde**, sem que a Infrastructure respeite nada. Ele só falha de verdade se provar o efeito: o notificador registrado antes é o que recebe o aviso quando o decorator troca de cérebro. Esperado: a §4 descreve a observação que faz o DI3 falhar antes de a fábrica existir, ou a T4 o declara entre os que nascem verdes, com a mutação que o derruba (trocar `TryAdd` por `Add` do silencioso). O mesmo vale para o D5/T4 quando listam o que falha.

## Sugestões
- **S1** — `tasks/US-007/plan.md` §10 D5 — a nota "e os testes DI cujo nome tem `BrainFactory`/`FallbackTutorBrain`" não corresponde a nenhum teste: DI1–DI5 não têm esses trechos no nome (DI2 é `...ResolvesFallbackBrainNamedAfterPrimary`). O filtro roda só B* e F*. Esperado: remover o trecho, ou dizer que os DI são provados por D2.
- **S2** — §10 D8 — o comando depende do Ollama e do modelo de 14B baixado; se a máquina do usuário não tiver, o D8 fica sem evidência. Esperado: o plano diz que, nesse caso, o Executor registra "não reproduzido" no §11 e o C3 vale como prova.
- **S3** — §4 DI1 — o teste renomeado deixa de afirmar `brain.Name == "ClaudeCode"` da versão antiga. O Name de cada provedor já é coberto pelos cérebros e pelo DI5; vale uma frase no plano dizendo isso, para não parecer enfraquecimento (checklist §3, "teste enfraquecido").
- **S4** — §2.3 — `BrainSwitchNotice.FromProvider` é preenchido, mas nenhum consumidor o usa (o console só mostra o destino). Registrar no plano que o campo existe para log e futuros consumidores, ou remover; isso evita a pergunta "código morto" na revisão do código.

## Pontos positivos
- ADR-010 versionado como Proposto antes do código, com a política no Core (R4) e o aviso por porta (R2); a prosa da ARCHITECTURE §6.3 é corrigida contra a RN-BRAIN08.
- Cada tarefa declara quais testes nascem verdes e a mutação que prova o Red/Green; as mutações de T1, T3, T4 e T5 são coerentes com o código atual.
- Os limites L1–L4 listam consumidores e o efeito em cada um; F15 fixa o comportamento da retentativa depois da troca.
- Inventário nominal do Grep para a renomeação e comandos D7/D12 com os dois lados demonstrados.
- `FallbackProvider` vazio no `appsettings.json` é justificado pela DoD D6 da spec, e há teste (C12).

## Aprendizados para o processo
- Contagem de testes por faixa ("B1–B6") sem lista nominal derivada da §4 tornou a lacuna invisível → reforçar AP-025: o Planejador deriva as faixas citadas em §3, §5 e DoD a partir da tabela §4, com conferência mecânica (listar os IDs da §4 antes de fechar o plano).
- Teste de DI que só resolve o serviço registrado nasce verde com facilidade → ao declarar o Red de testes de registro (`TryAdd`, "mantém o já registrado"), a §4 deve dizer o efeito observável, não só o tipo resolvido (relacionado a AP-010 e AP-025).

## Rodada 2

Data: 2026-10-22
Escopo revisado: `tasks/US-007/plan.md` (commit e436cb7, que mudou só o plano: 10 inserções e 4 remoções), `tasks/US-007/plan-review.md` (rodada 1) e a seção 12 do plano com as respostas do Planejador.
Resultado: APROVADO (0 bloqueantes)

### Itens da rodada anterior
- [x] **B1** (B5 ausente do mapa) — Resolvido em e436cb7. A §4 passou a ter `BrainFactoryTests` B5 `Create_WhenFallbackProviderIsUnknown_ThrowsNamingValueAndAcceptedProviders`, com projeto, classe e método. Os IDs da §4 são B1–B6 e batem com a §3, a T4, a D5 e a P10. B5 invalida só o reserva e B4 só o `Provider`, então os dois papéis estão cobertos (AP-047).
- [x] **B2** (DI3 nascia verde) — Resolvido em e436cb7. O DI3 registra um `RecordingBrainSwitchNotifier` e dois `StubTutorBrain` com chave (o primário lança `UsageLimitReached`, o reserva responde `t1`), resolve `ITutorBrain` e espera a resposta `t1` e exatamente um aviso no notificador registrado. Antes da T4 ele falha. A T4 lista DI3 entre os que falham, só DI1 nasce verde entre os DI, e a mutação `TryAddSingleton` → `AddSingleton` do notificador silencioso derruba o DI3. Dependências vêm de T2 e T3.

### Verificações
Todas ✅ (cenários BDD no mapa, P* com teste e tarefa, DoD e cobertura ≥ 80%, R1–R4/RN/CLAUDE.md/ADR Proposto, escopo e adiamentos, consistência §3×§4×§5×§6 com contagens B1–B6, F1–F15, DI1–DI5, C1–C12, V1–V6, ordem das tarefas preservando o Red, AP-016/015/047/039/035/050, sincronia ARCHITECTURE × CLAUDE.md, viabilidade no SDK). AP-051, AP-032 e AP-052: N/A.

### Bloqueantes
Nenhum.

### Sugestões (pendentes, o usuário não as aceitou nem recusou)
- **S1** — §10 D5: o texto "e os testes DI cujo nome tem `BrainFactory`/`FallbackTutorBrain`" não corresponde a nenhum teste. Remova o trecho ou diga que os DI são provados pela D2.
- **S2** — §10 D8: diga que, sem o Ollama e o modelo, o Executor registra "não reproduzido" na §11 e o C3 vale como prova.
- **S3** — §4 DI1: o teste renomeado deixa de afirmar `brain.Name == "ClaudeCode"`. Registre que o `Name` de cada provedor já é coberto pelos cérebros e pelo DI5.
- **S4** — §2.3: `BrainSwitchNotice.FromProvider` não tem consumidor. Justifique o campo (log e consumidores futuros) ou remova.

### Aprendizados para o processo
- Sugestão: exigir que o Planejador liste os IDs da §4 antes de fechar o plano (AP-025) e descreva o efeito observável em testes de `TryAdd` (AP-010).
