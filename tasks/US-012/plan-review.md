# Revisão do plano — US-012 Velocidade da fala configurável e ajustável na sessão

Data: 2026-10-27
Escopo revisado: `tasks/US-012/plan.md` (commit 6ec4bba) contra `specs/sprints/SPRINT_2.md` (US-012), RN-TTS02, RN-CONV06, `ARCHITECTURE.md` e o `src`/`tests` atuais. Li o plano inteiro, mas não reli ADR-012, `CLAUDE.md` nem as skills `tdd-dotnet` e `bdd-para-testes`. Conferi no código só os pontos citados abaixo.
Resultado: APROVADO (0 bloqueantes)

## Verificações
| Item | Resultado |
|---|---|
| Todo cenário BDD no mapa §4 (projeto, classe, método `Metodo_Cenario_Resultado`) | ✅ Os 4 cenários da spec e os 2 novos (§2.6) têm teste. |
| P* com efeito observável com teste (§4) e tarefa (§5) | ✅ P1, P2, P3, P5, P6 e P7 têm teste mapeado. P4 não tem efeito observável. |
| DoD §10 cobre a DoD da US, com comandos verificáveis e cobertura do Core ≥ 80% | ✅ D5, D6 e D7 da spec estão cobertas. D3 traz o reportgenerator. |
| Conformidade com R1–R4, RN-* e regras do CLAUDE.md; ADR | ✅ A regra da faixa fica no Core (R4). O Core usa só BCL. Ficou sem ADR por ser consequência do ADR-012, e as alternativas estão na §2.1. |
| Escopo e adiamentos (AP-022, AP-040) | ✅ O fora de escopo é explícito. Os "limites aceitos" listam consumidores e efeito. |
| Consistência §3 × §4 × §5 × §6 | ✅ Os nomes da §5 batem com a §4. Nenhum pacote novo. |
| Ordem das tarefas preserva o Red | ✅ O esqueleto em T1, T3 e T4 evita testes que nasçam verdes. Os 4 casos que nascem verdes têm mutação para provar o Red (T5) ou justificativa na conferência de Red da §4. |
| Transformações com teste nos dois lados do limite (AP-016) | ✅ A faixa tem 0.49, 0.5, 1.5 e 1.51. `Format` tem 1, 2 e 3 casas, mais o corte em 3 e o teste em pt-BR. |
| Afirmações condicionais da §2 com teste (AP-047) | ✅ A §4 mapeia cada uma. |
| Filtros da DoD com exclusão de `Live*` e `--list-tests` (AP-064) | ✅ Estão em D5 e D7, e a alteração da spec está em §2.6 itens 6 e 7. |
| Inventário e texto antigo (AP-050, AP-035) | ✅ Conferi no código: o antigo `Available commands: /help, /mode, /quit` está só em `ChatSessionTests.cs:33`. D9(b) acha 5 linhas no `develop`: `SPRINT_2.md` linhas 164, 169, 174, 181 e 183. |
| Alterações na `ARCHITECTURE.md` e sincronia | ✅ Os textos antigos existem em `ARCHITECTURE.md` (§3 linha 57; §7 linhas 254 e 256). Nenhuma linha de `CLAUDE.md` ou de skill muda. |
| Viabilidade técnica | ✅ `TtsOptions.MinSpeed`, `MaxSpeed` e `DefaultSpeed` existem como `const float`. `ReplySpeaker` lê `options.Value.Speed` em um único ponto (linha 36). |
| `ValidateOnStart` em várias Options e caixa diferente (AP-039) | N/A: nenhuma Options nova. |
| Evidências, ordem de commits, §12 | N/A: é revisão de plano. |

## Bloqueantes
Nenhum.

## Sugestões
- **S1** — `plan.md` §2.1, `SpeechSpeed.Change` — a decisão de aceitar `float.TryParse` com `AllowLeadingSign` fica coberta pelas linhas `-1` e `-Infinity`. Falta um caso de `+1.2` (sinal positivo aceito ou recusado), que hoje é um comportamento não declarado. Esperado: o plano diz o que acontece com `+1.2` e o Executor cobre com uma linha do Theory, ou então tira `AllowLeadingSign`, porque `-1` já cai em `OutOfRange` de qualquer jeito.
- **S2** — `plan.md` T4 — a asserção `Inputs` vazio (RN-CONV06) existe para os valores válidos, fora da faixa e não numéricos, mas não para `/speed` sem argumento na §4. Esperado: a linha de "sem argumento" também confere que nada vai ao cérebro.
- **S3** — `plan.md` §10 D8 — o comando roda `dotnet run` com a voz desligada. Convém o plano dizer que o cérebro não é acionado (só comandos locais), para o Executor não depender do Claude Code nessa prova.
- **S4** — `plan.md` §5 — a referência "Escolhas de forma: §12 em <hash>" segue a convenção das US anteriores. Mas a §12 deste plano é "Comentários do usuário" e a §11 é "Evidências". Esperado: o Executor registrar as escolhas de forma em local que o Revisor consiga conferir (AP-014/055), com hash anterior ao `feat` correspondente, mesmo que só haja "nenhuma".

## Pontos positivos
- A regra da faixa fica no Core (`SpeechSpeed`), com singleton compartilhado entre `ReplySpeaker` e `ChatSession`.
- Ordem do TDD bem pensada: o esqueleto de T3 e o singleton provisório de T4 mantêm o Red.
- Cobertura de cultura (pt-BR) em Core e Console, e tratamento explícito de `NaN` e `Infinity`.
- Mensagens de erro dizem o que está errado, o que se espera e que a velocidade anterior continua.
- A mudança de texto da spec (`vejo` para `a saída contém`) tem texto antigo e novo citados, e D9 prova que o comando acha o antigo.
- Limites aceitos listam consumidores e efeito.

## Aprendizados para o processo
- Nada novo. O plano já aplica AP-054, AP-064, AP-050 e AP-035.
