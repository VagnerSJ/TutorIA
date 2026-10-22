# Revisão — US-006 Brain Ollama (IChatClient, falhas, tempo máximo, tolerância 1 ou 2 perguntas)

Data: 2026-10-22
Commits revisados: 90e4741..b8d04cb (49 commits; `develop..HEAD`)
Resultado: APROVADO (0 bloqueantes)

## Verificações
| Item | Resultado |
|---|---|
| Build sem warnings | ✅ 0 avisos, 0 erros |
| Testes (exceto LiveBrain) | ✅ 366 passaram (Core 138, Infrastructure 173, Console 55), 0 falhas, menos de 1 s por projeto |
| Cobertura de linhas do Core | 100% (linha geral 90,1%) |
| Cenários BDD com teste | n/n. Conferi o mapa da §4 contra os arquivos do diff e contra as contagens de D2 e D11. Não reli cada método um a um. |
| Teste antes do código (ordem de commits) | ✅ Todos os pares `test` → `feat` estão na ordem certa: 7c8ed13→1acc4a9, ea4f5a2→5753ead, 4e44616→d8c10fb, b5e5cf6→7c3c18b, 6ef78b0→ec02909, 6a24b4f→8cf1a78, 6ee8e54→e0931d0, 87112ad→b38b907. |
| Evidências da DoD conferem | ✅ Reproduzidos e iguais: D1, D2, D3b (Core 100%, geral 90,1%), D4 (exit 0), D14 (`0`). D5 a D10 e D19 dependem de Ollama, Claude Code ou `dotnet run`: evidência do Executor, não reproduzida. A coerência com o código está ok: D5 mostra 3 rodadas com `exit=0` e a camada medida é o `ConversationService` com a retentativa. |
| Intenção antes do código (spec/ADR → código) | ✅ A spec e o ADR-009 vieram antes do T1. A tolerância (`BUSINESS_RULES.md`, RN-FEED01) foi para a spec em 15b4291 e o plano foi revisado antes do 6ee8e54. O texto do reforço dos prompts foi registrado em 2cab4b6, antes do 87112ad e do b38b907. |

Outras conferências:
- `git status` está limpo.
- Nenhum `Skip` novo nos testes.
- Core sem pacotes novos.
- Prompts: o reforço está em `format.json.md` e `format.tags.md`, e `tutor.md` não mudou.
- O validador usa as constantes `_minQuestions` e `_maxQuestions`, e a mensagem de rejeição cita a tolerância.

## Itens da rodada anterior
N/A. Esta é a primeira revisão de código da US-006. As cinco rodadas de revisão do plano foram tratadas em commits anteriores.

## Bloqueantes
Nenhum.

## Sugestões
- **S1** — `tests/EnglishTutor.Infrastructure.Tests/Brain/OllamaFixtureRecorderLiveTests.cs`, método `SendAsync_WithGrammarErrorSentence_SavesRealChatAndModelNotFoundExchanges`, última asserção (`Count(ReplySentences.IsQuestion).Should().Be(1)`).
  - **Problema:** este é o risco apontado pelo Executor, e ele procede. O teste roda no filtro do D5 (`FullyQualifiedName~Ollama`, 5 testes por rodada). Ele chama o adaptador direto, sem a retentativa do serviço, e exige exatamente 1 pergunta. O validador agora aceita 2 perguntas, então uma resposta de 2 perguntas deixa o teste vermelho e o D5 inteiro falha, mesmo com o formato "tolerado" e sem `RawFallback` no teste de conversa.
  - **Evidência atual:** nas 3 rodadas do D5 o modelo deu 0/5 primeiras tentativas inválidas, então ficou verde. Isso não elimina a oscilação.
  - **Plano:** a decisão P17 manteve `Be(1)` de propósito, porque a fixture documenta o caso pedido. O §7 diz que "gravadores continuam exigindo 1 pergunta". O D5 diz "tolerância zero" sem apontar que o gravador é a única oscilação residual fora da camada medida.
  - **Esperado:** o plano e o D5 devem dizer que o gravador é o ponto frágil. O vermelho dele por 2 perguntas não é regressão do adaptador. Ou então a asserção de contagem deve sair do caminho que também alimenta o D5. Por exemplo: o gravador só grava e a conferência de "1 pergunta" fica no teste da fixture já gravada, que é determinístico.
  - **Por que não é bloqueante:** é decisão registrada (P17), a spec não exige o contrário, e o D5 foi verde nas 3 rodadas. A regra do AP-032 sobre registrar o risco de oscilação e quem decide quando ficar vermelho está só parcialmente cumprida para este teste.
  - **Se ficar vermelho:** a regra do §10 D5 manda parar e classificar na §12. Quem decide é o usuário.
- **S2** — `tasks/US-006/plan.md` §11, evidência do D5. O texto diz que "a retentativa não foi usada" nas 3 rodadas, porque não houve primeira tentativa inválida. Por isso o D5 verde não exercita a retentativa ao vivo. Ela é coberta só pelos testes unitários e pela mutação do T13. Convém deixar isso explícito para a US-007 não herdar uma garantia que não foi medida.
- **S3** — `ollama-smoke.txt` registra `expected corrections found: 4/5` nas 3 rodadas, e a fumaça só exige o formato. Está de acordo com a tolerância da US (e a mesma de 4/5 do Claude Code), mas fica sem margem. O Documentador pode registrar isso como risco para o `eval` da US-009.

## Pontos positivos
- Camadas bem separadas: tolerância no Core (R4), adaptador sobre `IChatClient` registrado por configuração (R3), e o seletor provisório com marca `Provisional selector` verificável (D16).
- Mutações registradas por tarefa e descritas pelo efeito, incluindo "mutação depois do Green" onde o Red não tem o que quebrar.
- Controles D15c e D20c antes da mudança, que provam que a busca de "nenhuma linha" enxerga o texto antigo (AP-035).
- A cobertura de 100% do Core inclui os dois lados do limite de 1 e 2 perguntas (0, 1, 2, 3 e 4).
- Sem caracteres invisíveis no diff (D14 = 0).
- Fixtures gravadas com o próprio adaptador (AP-048). O README da pasta documenta que `chat-request.json` ficou com o prompt antigo.

## Aprendizados para o processo
- Um teste `LiveBrain` de gravação que valida conteúdo de saída do modelo, num filtro de DoD com "tolerância zero", é fonte escondida de oscilação. A tolerância foi relaxada no verificador (Core), mas não na asserção do gravador. → Regra sugerida para a skill `bdd-para-testes` ou `checklist-revisao` §0: ao relaxar uma validação por tolerância, listar todos os testes `LiveBrain` do filtro da DoD que ainda assertam a regra original (inclusive gravadores). Cada um deve ter decisão explícita (manter e aceitar o risco, ou mover a asserção para a fixture gravada). Relacionado ao AP-032 e ao AP-040.
- O D5 verde sem nenhuma retentativa usada mede o adaptador com prompts reforçados, não a retentativa. → O critério `LiveBrain` deve dizer se precisa ter exercitado o mecanismo medido ou se basta o teste unitário (AP-032).
