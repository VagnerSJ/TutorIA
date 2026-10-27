# Revisão — US-012 Velocidade da fala configurável e ajustável na sessão

Data: 2026-10-27
Commits revisados: 6ec4bba..9b45b27 (17 commits em `develop..HEAD`)
Resultado: APROVADO (0 bloqueantes). Resta o D6, manual, que é do usuário.

## Verificações
| Item | Resultado |
|---|---|
| Build sem warnings | ✅ 0 avisos, 0 erros |
| Testes (exceto LiveBrain/LiveVoice) | ✅ 822 passaram (Core 359, Infrastructure 322, Console 141), 0 falhas, 0 ignorados |
| Cobertura de linhas do Core | 98,2% (mesclado com reportgenerator, fora do repositório; total da solução 92,6%) |
| Cenários BDD com teste | 10/10 (os 8 da spec, incluindo os 2 novos, e o cenário "Velocidade configurada" da spec; todos os métodos da §4 existem no código). Os 8 da spec: configurada, mudar na sessão, fora da faixa, não é número, sem argumento, sem voz, `/help`, `/speed` sem voz. |
| Teste antes do código (ordem de commits) | ✅ Cada par test→feat está na ordem: f71a6f3→a9093cd, 1059f79→04231e9, 61429eb→3a157aa, c2f9a22→bbc3fc4, 149fd0e→0f9bccc |
| Evidências da DoD conferem | ✅ D1 a D5, D7, D9 e D10 batem com o que obtive. D6 e D11 pendentes (manual). D8 é evidência do Executor, não reproduzida. |
| Intenção antes do código (spec/ADR → código) | ✅ Os commits de plano, spec e ARCHITECTURE (6ec4bba, ab51bf0, 79bd069, 2a7ac6b, 63748dd, 6f7b78a) vêm antes do primeiro código (f71a6f3). Não há ADR novo, como o plano justifica. |

Confirmei também:
- `dotnet format --verify-no-changes` terminou com código 0.
- A busca por caracteres invisíveis no diff deu contagem 0.
- `git status` estava limpo.
- A subseção "Escolhas de forma" (§11) cita os commits `test`, todos anteriores aos `feat`.
- Nada fora do escopo entrou no diff, e as tarefas T1 a T5 estão marcadas `[x]`.
- O `ChatSession` não encaminha `/speed` ao `inputHandler` (RN-CONV06).
- O `ReplySpeaker` lê `speed.Current` a cada fala.

## Itens da rodada anterior
Não se aplica (primeira revisão de código). As sugestões S1 a S4 da revisão do plano foram incorporadas, conforme a §12 do plano.

## Bloqueantes
Nenhum.

## Sugestões
- **S1** — `tasks/US-012/plan.md`, §11, D6 e D11: seguem pendentes. Antes do merge em `develop`, o usuário deve rodar `/speed 0.7` e `/speed 1.4` com Piper real, confirmar a diferença audível e registrar. Depois confira `git status --short` (D11).
- **S2** — `tasks/US-012/plan.md`, §11, mutações da T4: a mutação "`OutOfRange` aplica o valor" não foi repetida no console. A justificativa é razoável, porque `Current` tem `private set` e a mutação equivalente no Core já foi coberta.

## Pontos positivos
- A regra da faixa mora no Core (`SpeechSpeed`) e o singleton é compartilhado entre `ChatSession` e `ReplySpeaker`, o que respeita R4.
- Há testes em pt-BR nos dois níveis, `NaN`/`Infinity` explícitos e os dois lados dos limites.
- Os testes de ponta a ponta nasceram em Red por mutação e estão documentados.
- O D7 foi preservado: o corpo do teste da US-011 não mudou.
- As evidências são resumidas, sem saída bruta.

## Aprendizados para o processo
- Nenhum erro novo nesta US. O padrão de esqueleto que guarda o parâmetro em campo para evitar CS9113 (T1, T3 e T4) pode virar nota na skill `tdd-dotnet`, se ainda não estiver lá.
