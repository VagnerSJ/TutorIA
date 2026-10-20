# Revisão — US-004 Conversar por texto com o professor via Claude Code

## Rodada 1

Data: 2026-10-19
Commits revisados: 75a726d..7b87afb
Resultado: REPROVADO (1 bloqueante)

### Verificações
| Item | Resultado |
|---|---|
| Build sem warnings | ✅ 0 avisos, 0 erros |
| Testes (exceto LiveBrain) | ✅ 216 passaram (Core 103, Console 41, Infrastructure 72), 0 falharam, cerca de 0,8 s de execução |
| Cobertura de linhas do Core | 100% (Infrastructure 83,5%, Console 93,4%, total 88,2%) |
| Cenários BDD com teste | 9/9 |
| Teste antes do código (ordem de commits) | ✅ |
| Evidências da DoD conferem | ✅ para D1, D2, D3/D3b, D4, D11 e D15, que reproduzi. D5, D6, D7, D9 e D12 são evidência do Executor, não reproduzida (AP-008; o D5 não foi rodado por decisão do usuário, para não gastar a assinatura). O D5 está registrado como ❌ intermitente, ver B1 |
| Intenção antes do código (spec/ADR → código) | ✅ |

#### Checklist (`checklist-revisao`)

| Seção | Item | Resultado |
|---|---|---|
| 1 | [O] Todo cenário BDD tem teste (§4 × código) | ✅ 9/9; os nomes dos métodos batem com a §4 |
| 1 | [O] Comportamento bate com os cenários | ✅ offline. ❌ no critério "5/5 no formato" do D5 ao vivo (B1) |
| 1 | [O] RN-* respeitadas, testes citam a regra | ✅ (RN-BRAIN01/05/06, RN-CONV01/03/06, RN-FEED01–05) |
| 1 | [O] Nada fora do escopo sem registro | ✅ |
| 1 | [O] Spec/plano/ADR antes do código | ✅ f42d0d7 e 5917a1d antes da T1; ADR-007 Aceito; decisão do usuário (08f5fdd) antes da mudança de prompt (2cb0f7a) |
| 1 | [O] DoD rodada, evidência colada e conferida | ✅ evidência presente e honesta; o D5 está vermelho (B1) |
| 1 | Comentários do usuário respondidos | ✅ (com o desvio de escopo de commit da S2) |
| 1 | [O] Escolha de forma com texto exibido no §12 antes do feat | ✅ E1 em 9e49cf0, antes de 369608a |
| 2 | [O] R1: Core sem dependências externas | ✅ só um `TryAddSingleton<ConversationService>` e `/mode` em `LocalCommands` |
| 2 | [O] R4: regra no Core | ✅ |
| 2 | [O] R3: adaptador implementa porta e é registrado | ✅ (fixo em ClaudeCode até a US-007, decisão P17 aceita) |
| 2 | [O] ADR + ARCHITECTURE atualizados | ✅ ADR-007, ARCHITECTURE §3/§5/§6.3, ADR-002 com a latência |
| 3 | [O] Build sem warnings / suíte verde / Core ≥ 80% | ✅ / ✅ / ✅ |
| 3 | [O] Nenhum teste apagado, ignorado ou enfraquecido | ✅ só o renomeado `RunAsync_WhenUserTypesText_ShowsTutorNotConnected` → `RunAsync_WhenProviderIsOllama_ShowsTutorNotConnected`, justificado na §4 |
| 3 | [O] Suíte unitária sem rede, GPU ou processo real | ✅ `ProcessRunner` só nos testes `LiveBrain`; `CreateOptions` registra `ScriptedTutorBrain` |
| 3 | Determinismo | ✅ `ManualTimeProvider`, sem `Sleep`/`Delay`; variável de ambiente com nome único |
| 4 | [O] `CancellationToken` na cadeia, sem `.Result`/`.Wait()` | ✅ |
| 4 | [O] Sem `catch` que engole exceção; OCE propaga | ✅ os três `catch` silenciosos (`JsonException` em `ClaudeCodeOutput.TryParse`, `IOException` em `ProcessRunner.WriteStandardInputAsync`, `InvalidOperationException` em `ProcessRunner.KillTree`) são intencionais, comentados e previstos na §2.1/§2.5 |
| 4 | [O] Processos com `ArgumentList`, timeout e encerramento da árvore | ✅ `ArgumentList` e `Kill(entireProcessTree: true)` no cancelamento; timeout N/A por decisão da spec (US-005, P6, §8) |
| 4 | Caracteres invisíveis (D15) | ✅ contagem 0 |
| 5 | [O] Sem segredo nem dado pessoal commitado | ✅ fixtures sem `Users`, `@`, `sk-ant` nem caminho pessoal |
| 5 | [O] `.gitignore` | ✅ nada novo a cobrir |
| 5 | [O] Sem `--bare`, sem ferramentas, pasta isolada | ✅ `--tools ""`, `--disallowedTools mcp__*`, `--strict-mcp-config`, `--permission-mode dontAsk`; system prompt fora da pasta de trabalho |
| 6 | [O] Commits convencionais, branch `feature/US-XXX_slug` | ✅ |
| 6 | Plano com tarefas `[x]` | ✅ T1–T17; T18 aberta por causa do D5 |
| 6 | Chaves novas no `appsettings.json` | ✅ `Brain:ClaudeCode:*` e `Conversation:MaxTurnsInWindow = 10` |

### Bloqueantes
- [ ] **B1** — `tests/EnglishTutor.Infrastructure.Tests/Brain/ClaudeCodeTutorBrainLiveTests.cs` (`SendAsync_WithFiveKnownErrors_FindsAtLeastFourCorrectionsAllInFormat` e `SendAsync_WithGrammarErrorSentence_RepliesUpToThreeSentencesEndingWithQuestion`) × `prompts/tutor.md`, `prompts/format.json.md` e `prompts/format.tags.md`. O D5 da DoD da US-004 (`specs/sprints/SPRINT_1.md`, "DoD extra": "5/5 no formato") não está cumprido. Segundo a §11 e a §12 E4, depois do reforço de prompt (2cb0f7a) o D5 ficou verde em 2 de 5 rodadas. As respostas fora do formato caíram de cerca de 20% para 5–10% por chamada. O motivo é sempre o mesmo: o `reply` traz uma frase depois da pergunta e o validador recusa com `reply must end with a question`. O teste do cenário 1 ("reply com no máximo 3 frases terminando em pergunta") também falha de vez em quando pelo mesmo motivo. A evidência está correta e o Executor parou como o plano manda. O achado é só que a DoD da spec, como está escrita hoje, não é satisfeita. **Esperado:** o D5 da US e o da §10 do plano verdes de forma reprodutível, ou um critério diferente que o usuário escolha e registre primeiro na spec (US-004 "DoD extra") e na §10, com nova evidência na §11. Exemplos de critério: medir no nível que o aluno vê, já com a retentativa do ADR-006, ou aceitar uma taxa. Pelo plano (§7, risco "Fumaça abaixo de 4/5", e §12 E3/E4), a decisão é do usuário: outro ajuste de prompt, mudança de critério ou troca de modelo. Este relatório não prescreve qual. (Spec US-004 DoD D5; checklist §1, "Comportamento implementado bate com os cenários" e "comandos da DoD".)

### Sugestões
- **S1** — commit 2cb0f7a (`docs(ai): require the tutor question to be the last sentence of the reply`). A decisão do usuário na §12 ("decisão do usuário sobre o E3") pedia um commit `feat(brain)` ou `fix(brain)`, mas a mudança de prompt entrou como `docs(ai)`. O próprio Executor registrou o desvio no E4. Os prompts mudam o comportamento do professor. Reescrever o histórico não é necessário, mas o desvio deve ficar registrado como erro de processo (ver "Aprendizados").
- **S2** — §12 E3 ("Herança das variáveis do app desktop") e `src/EnglishTutor.Infrastructure/Processes/ProcessRunner.cs` (`RunAsync`). O processo `claude` herda todo o ambiente de quem abre o console (`CLAUDECODE`, `CLAUDE_CODE_*`, `ANTHROPIC_BASE_URL`). Numa gravação, o nome do titular da conta chegou ao contexto do professor. Nada foi commitado, mas o risco de privacidade em tempo de execução ficou sem dono: o S11 da §7/§8 diz "sem US de destino". Pelo AP-022, o efeito esperado ("o professor não recebe dados da conta vindos do ambiente de quem abre o app") precisa de texto registrado na spec de uma US de destino (ex.: US-005 ou uma US nova), ou de um ADR Proposto.
- **S3** — §10 D5/D7 e §11. As evidências do D5 e do D7 foram obtidas "sem as variáveis herdadas", mas o comando da DoD não mostra como o ambiente foi limpo. Quem roda o D5 a partir do app desktop executa em outro ambiente e pode ver outros resultados. O comando, ou uma nota na §10, deveria dizer em que ambiente o resultado vale, para a evidência ser reproduzível.
- **S4** — `src/EnglishTutor.Console/Ui/TutorNotConnectedInputHandler.cs` (`ChangeMode`), com 42,8% de cobertura de linhas. A troca de modo com `Brain:Provider = Ollama` não tem teste com o handler real; o `ChatSessionTests` usa o `RecordingChatInputHandler`. O efeito ("com Ollama, `/mode job-interview` confirma e o modo exibido muda") é pequeno e o handler é provisório até a US-006, por isso fica só como sugestão.

### Pontos positivos
- A ordem Red → Green aparece em todos os pares `test`/`feat`. Os testes de caracterização (T2, T10, T16) tiveram Red por mutação temporária, registrada com a mensagem de falha (AP-017).
- `ClaudeCodeOutput.Read` segue exatamente a tabela da §2.5. Os casos `other-type`, `json-array` e `is_error` com código 0 usam um `structured_output` válido, de modo que o teste só passa se a regra certa for aplicada (B3 e S2 da revisão do plano).
- A regra de retomada do ADR-007 está bem coberta: 5 casos de "não estende a sessão", mais a retentativa do ADR-006 que retoma só com a correção de formato. A sessão é zerada antes de rodar o processo, então falha ou cancelamento não deixam estado inconsistente.
- A P19 (várias seções inválidas → `AggregateException`) foi tratada com filtro `when`. A mensagem de configuração inválida continua sendo escrita com `CancellationToken.None`.
- A evidência é honesta. O D5 foi marcado ❌ intermitente com números antes e depois do reforço. O Executor não mudou teste, validador nem critério para fazê-lo passar. A fixture que trouxe dado pessoal foi descartada e gravada de novo.
- As contagens (49 métodos / 65 casos novos, suíte com 216) batem com o que obtive (AP-031).

### Aprendizados para o processo
- **Critério ao vivo de "100%" sobre saída de LLM gera DoD intermitente.** O "5/5 no formato" mede o adaptador sem a retentativa que o produto tem (ADR-006), e por isso falha numa taxa que o aluno quase não percebe. → Sugestão para a skill do Planejador e para o `checklist-revisao` §0: na revisão de plano, todo critério `LiveBrain` deve dizer em que camada mede (adaptador ou serviço com retentativa) e qual tolerância aceita, e precisa sobreviver a N rodadas seguidas (ex.: 3) antes da aprovação. Vale registrar como AP novo em `docs/aprendizados.md`.
- **Escopo de commit diferente do que o usuário decidiu (S1).** É parecido com o AP-014/AP-027 (forma decidida e não seguida), agora no tipo do commit. → Na skill do Executor: quando a §12 fixa o tipo/escopo do commit, use esse; para divergir, registre na §12 **antes** do commit.
- **Ambiente herdado pelo processo filho afeta privacidade e reprodutibilidade (S2, S3).** → Sugestão para a seção "Processos externos" de `padroes-codigo`: o `IProcessRunner` deve deixar explícito quais variáveis de ambiente o filho recebe, e a DoD com processo real deve dizer em que ambiente roda. Adotar isso como regra é decisão de ADR.

## Rodada 2

Data: 2026-10-20
Commits revisados: 7b87afb..545ef2a (24 commits; branch contra `develop`: 79 arquivos)
Resultado: APROVADO (0 bloqueantes)

### Verificações
| Item | Resultado |
|---|---|
| Build sem warnings | ✅ 0 avisos, 0 erros |
| Testes (exceto LiveBrain) | ✅ 249 passaram (Core 134, Console 41, Infrastructure 74), 0 falharam. Cada projeto levou menos de 0,5 s. |
| Cobertura de linhas do Core | 100% (Infrastructure 83,5%, Console 93,4%, total 88,7%, igual ao colado no §11) |
| Cenários BDD com teste | 11/11 (os 9 da rodada 1 mais os 2 novos: "pergunta seguida de incentivo é aceita" e "reply com duas perguntas é inválido") |
| Teste antes do código (ordem de commits) | ✅ Cada par sai nesta ordem: `fcf85b2` e `cebf808`, `db3bafd` e `0ad07c0`, `31bf6c2` e `3f02faa`. `b82638b` e `ed41b9a` (testes) vêm antes dos prompts em `11514ab`. |
| Evidências da DoD conferem | ✅ Reproduzi D1, D2, D3/D3b, D4, D15, D16 (`scanned=133 named=3`, nenhuma linha), D17 (19 casos) e D18 (26 casos). D5, D6, D7, D9 e D12 são evidência do Executor, não reproduzida (AP-008). |
| Intenção antes do código (spec/ADR → código) | ✅ A spec (`ea8744d`, `79dce10`, `76f8146`) e a aprovação do plano (`d39ffab`) vêm antes da T18. A decisão do usuário sobre o E5 (`7bc0ccf`) vem antes de `279047e` e `f237c82`. |

Detalhes que conferi:
- **D2:** a contagem (249) bate com a §10 e com o §11.
- **D15:** a contagem de caracteres invisíveis em `git diff develop...HEAD` deu 0.
- **D6/D9:** conferi só a coerência com o código. Os prompts, o cabeçalho e a mensagem de troca de modo batem com o que o §11 descreve.
- **D14:** `git status` está limpo. O D16 não achou nenhum padrão antigo.
- **Controle D16c (T18):** foi registrado em `c482f1e`, antes do Red, com todos os padrões esperados presentes. Isso fecha a S3 da Rodada 4 do `plan-review.md`.

### Itens da rodada anterior
- **B1 — D5 intermitente: resolvido** em `f237c82` (prompts), com `279047e` (diagnóstico no teste) e a evidência em `545ef2a`.
  - **Causa tratada:** a regra mudou para "exatamente uma pergunta" (spec do usuário em `ea8744d`, código em `3f02faa`). Com isso a frase de incentivo depois da pergunta deixou de ser recusada.
  - **Evidência do Executor (§11, §12 E6):** D5 com 5 rodadas seguidas `exit=0` no ambiente limpo do §10, 3 testes verdes em cada, nenhuma rodada vermelha. O critério da spec não foi enfraquecido: continua 5/5 no formato e ≥ 4 correções por rodada.
  - **Ordem do processo:** o E5 (D5 vermelho na rodada 1 da T23) foi registrado e o Executor parou. A decisão do usuário (`7bc0ccf`) veio antes das duas mudanças. Os tipos de commit são os que a decisão fixou (`test(infra)` e `feat(brain)`).
  - **Ressalva:** essa evidência eu não reproduzi, por decisão do pedido (custa assinatura). Ver S5.
- **S1 — tipo do commit `docs(ai)` para uma mudança de prompt:** não pede ação. O desvio ficou registrado no E4. Desta vez a regra foi seguida (`feat(brain)` em `11514ab` e `f237c82`). O destino que resta é o registro como aprendizado, pelo Documentador.
- **S2 — privacidade do ambiente herdado pelo processo `claude`:** aceita de forma explícita pelo usuário (plano §7 e §8, resposta ao item S7 da Rodada 4). Fica sem US de destino, sem ADR agora e sem promessa para outra US, com motivo, mitigações e condição de revisão escritos no §7. Como não há trabalho transferido, o AP-022 não exige texto na spec de outra US. Destino claro.
- **S3 — ambiente do D5/D7 não era reproduzível: resolvida.** A §10 agora traz a limpeza das variáveis (`CLAUDECODE`, `ANTHROPIC_BASE_URL`, `CLAUDE_CODE_*`) dentro do comando do D5 e a descrição do ambiente do D6. O §11 lista as 4 variáveis que sobraram (`CLAUDE_AGENT_SDK_*`, `CLAUDE_PID`, `CLAUDE_PREVIEW_CLASSIFIER_FLOOR`).
- **S4 — teste do `TutorNotConnectedInputHandler.ChangeMode` (42,8% de cobertura):** pendente por decisão do usuário. Está em item próprio na §8 ("segue para decisão do usuário") e fora de qualquer tarefa. Continua sugestão, o handler é provisório até a US-006. Não bloqueia.

### Bloqueantes
Nenhum.

### Sugestões
- **S5** — `tasks/US-004/plan.md` §11 ("Antes e depois") × `tests/EnglishTutor.Infrastructure.Tests/Brain/ClaudeCodeTutorBrainLiveTests.cs` (`SendAsync_WithFiveKnownErrors_FindsAtLeastFourCorrectionsAllInFormat`). O D5 verde se apoia em 5 rodadas de 8 chamadas (40 respostas), contra uma falha anterior em cerca de 5 a 10% das chamadas. O próprio Executor registrou que isso "é compatível com uma taxa baixa, mas não a prova". O critério do D5 é tolerância zero no adaptador, sem a retentativa do ADR-006, e pode oscilar de novo. O `because` agora mostra a frase, a resposta recusada e o motivo, o que facilita a próxima classificação. **Esperado:** se o D5 voltar a ficar vermelho (por exemplo na US-005 ou na US-006), o usuário decide então se mede no nível do serviço com retentativa ou se aceita uma taxa, com registro prévio na spec. O Documentador deve anotar esse risco residual no `docs/us/US-004.md`.
- **S6** — `tests/EnglishTutor.Core.Tests/Brain/TutorReplyValidatorTests.cs` (`Validate_WhenReplyEndsWithQuestionFollowedBySpaces_IsValidAndTrimmed`). O comentário diz `Cenário: reply sem pergunta (apoio, P6)`, mas o teste cobre o aceite de "pergunta no fim seguida de espaços". É efeito da troca mecânica do nome antigo do cenário (S2 da Rodada 4). O comentário correto seria "pergunta seguida de incentivo é aceita (apoio)" ou "reply sem pergunta (aceite com espaços finais)". É só rótulo, sem efeito observável.
- **S7** — o D17 e o ReplySentences tratam `Mr.` e `Dr.` como fim de frase quando a próxima palavra começa em maiúscula (limite P22, aceito e registrado). No validador isso não traz efeito, porque só conta perguntas. Se um dia o teste do cenário 1 (`HaveCountLessThanOrEqualTo(3)`) reclamar de mais de 3 frases por causa de abreviação, a classificação (b) da T23 prevê levar a decisão ao usuário. Só para registrar, nada a fazer agora.

### Pontos positivos
- A mudança de RN-FEED01 percorreu a ordem inteira: spec do usuário, `plan.md`, revisão do plano (Rodadas 4 e 5), aprovação, depois Red, Green e Refactor (`99e2e5c` removeu a recusa antiga sem mudar comportamento).
- O Core só usa a BCL no `ReplySentences` e não usa `EndsWith('?')`, como o plano pediu. A contagem de perguntas fica no `TutorReplyValidator` (R4).
- `TutorReplyRejections.ReplyMustAskExactlyOneQuestion(reply, questionCount)` diz quantas perguntas encontrou e o que se espera ("Expected 1 to 3 short sentences with exactly one question..."). O teste de mensagem confere os dois lados da contagem (0, 2 e 3) com um trecho que só o projeto produz (AP-015, AP-016).
- As bordas do separador estão cobertas (`?` entre aspas, reticências em minúscula e maiúscula, `e.g.`, decimal, `?!`, quebra de linha, texto em branco). O cenário "duas perguntas" cobre `Really?! What happened next?` e `"Really?" Then what...`.
- O Executor parou na primeira rodada vermelha do D5 (E5) em vez de repetir para obter uma verde. O diagnóstico extra (20 chamadas) ficou como diagnóstico temporário, registrado e não commitado. O ajuste do `because` usa `becauseArgs`, e a razão (chaves do JSON e `string.Format`) está explicada no E6.
- Os docs que repetiam a regra antiga foram atualizados em bloco: ARCHITECTURE, ADR-002, ADR-005 (com "Revisado"), US-002, PRODUCT e a skill `tdd-dotnet`. O D16 prova isso por texto, e o D16c mostra que o comando enxerga o texto antigo.
- Nenhum teste foi apagado ou ignorado. Os renomeados foram mapeados no plano (§4, "Testes existentes alterados").

### Aprendizados para o processo
- **Desvio de tipo de commit (S1) não se repetiu.** Depois do registro da rodada 1, o Executor seguiu o tipo fixado pela decisão do usuário nas duas mudanças de prompt. → Fica como aprendizado fechado. O Documentador transfere para `docs/aprendizados.md` a regra "quando a §12 fixa o tipo ou escopo do commit, use esse; para divergir, registre antes".
- **Critério `LiveBrain` de "100%" sobre saída de LLM** (rodada 1) foi resolvido aqui por mudança de regra e reforço de prompt, e não por mudança de critério. O risco de oscilação continua, ver S5. → Reforçar a sugestão da rodada 1 para o `checklist-revisao` §0: todo critério `LiveBrain` deve dizer a camada medida (adaptador ou serviço com retentativa), a tolerância e quantas rodadas seguidas precisa sobreviver antes da aprovação do plano.
- **Rótulo de comentário alterado por substituição mecânica** (S6) deixou um comentário que não descreve mais o teste. → Na skill `tdd-dotnet`, ao trocar o nome de um cenário, reler cada comentário alterado contra o corpo do teste, não só o texto antigo.
