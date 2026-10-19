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
