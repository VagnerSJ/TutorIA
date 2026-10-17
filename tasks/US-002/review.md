# Revisão — US-002 Contrato do professor e parser único da resposta

## Rodada 1

Data: 2026-10-17
Commits revisados: d5694e5..a7ecdd0 (plano aprovado em 4c89347; ARCHITECTURE/ADR-005 em 0c0c421)
Resultado: APROVADO

### Verificações
| Item | Resultado |
|---|---|
| Build sem warnings | ✅ (`dotnet build --no-incremental`: 0 avisos, 0 erros, sem CS8775) |
| Testes (exceto LiveBrain) | ✅ 93 passaram (Core.Tests 47, Infrastructure.Tests 24, Console.Tests 22) |
| Cobertura de linhas do Core | 100% (Infrastructure 81,6%, informativo; total 86,2%) |
| Cenários BDD com teste | 6/6 |
| Teste antes do código (ordem de commits) | ✅ |
| Evidências da DoD conferem | ✅ |
| Intenção antes do código (spec/ADR → código) | ⚠️ (ADR-005 e ARCHITECTURE antes do código; escolhas de forma registradas na §12 só depois dos `feat`, ver S2) |

#### Detalhe das verificações

- **Spec e regras.** Os 6 cenários da US em `specs/sprints/SPRINT_1.md` têm teste no parser e, quando cabe, no validador ou no formatador. Os 31 métodos e 44 casos da §4 existem no código com os nomes previstos. RN-FEED01/02 estão no Core (`TutorReplyValidator`). O parser cuida só do formato (R4, ADR-005). Os testes citam a regra no comentário. Nada foi implementado fora do escopo da §8.
- **Arquitetura.** O Core continua só com `Microsoft.Extensions.Options` e `Microsoft.Extensions.DependencyInjection.Abstractions` (`CoreDependencyTests` verde). R1 e R4 são respeitadas. R3 é N/A: o parser não é adaptador de porta, e o registro por DI fica para a US-004 (§2 "Alternativas descartadas"). A ARCHITECTURE §3/§4/§6.2/§10 e a skill `tdd-dotnet` (linhas 28 e 90) foram alteradas em 0c0c421, antes da T1, como pede a §2.4.
- **Ordem TDD.** Todos os pares estão como `test` e depois `feat`: 877fbe6→dbe7b8a, 68c5c73→933199b, c1024be→df0f68a, 3e7705d→731399b, 6c9a49f→ea150a5, fb7fdeb→2ee4cb0, com o refactor 2507990 por último. Os commits de teste contêm só testes e esqueletos com `NotImplementedException`. Na T6 (ea150a5), `ParseTags` lança `NotImplementedException("Tag format: implemented in T7.")`, como a §5 exigia (B2 do plano).
- **Qualidade.** Não há `catch` genérico. Só `JsonException` é capturada, e ela é convertida em rejeição. O log é estruturado (`[LoggerMessage]` 2000) e não registra texto cru nem conteúdo do aluno. Os tipos são `sealed`/`sealed record`. Não há I/O nem método assíncrono, então `CancellationToken` é N/A.
- **Segurança.** As fixtures são exemplos da spec, sem dado pessoal. Não entrou nenhuma configuração local nova.
- **Caracteres invisíveis.** `git diff develop...HEAD | LC_ALL=C grep -c ...` imprime `0`, e o D11b não lista nenhum arquivo.
- **Git.** Os commits são convencionais e têm escopo. A branch segue `feature/US-002_contrato-parser-resposta`, e T1–T8 estão marcadas `[x]`.

#### Evidências (§11) × o que eu obtive

| # | Evidência do Executor | Minha execução |
|---|---|---|
| D1 | 0 avisos, 0 erros | Igual |
| D2 | 93 verdes (47/24/22) | Igual |
| D3/D3b | Core 100%, Infrastructure 81,6%, total 86,2% | Igual. O `reportgenerator` foi gerado em `%TEMP%`, fora do repositório |
| D4 | Sem diferenças | Igual (código de saída 0) |
| D5 | 24/24 no Infrastructure.Tests | Igual |
| D6 | 4/4 | `TutorReplyRejectionsTests` com `CoreDependencyTests`: 7/7 (4 + 3). As 4 mensagens coladas batem com o catálogo |
| D7 (testes) | 3/3 | Igual |
| D7/D8 (`dotnet list package`), D9 (`dotnet sln list`) | Conforme §11 | Comando fora da minha lista permitida: **evidência do Executor, não reproduzida**. Batem com os `.csproj` e o `EnglishTutor.slnx` que eu li |
| D10 | `git status` vazio | Igual |
| D11a/b | `0` / sem saída | Igual |

### Bloqueantes
Nenhum.

### Sugestões

- **S1** — `tests/EnglishTutor.Infrastructure.Tests/Brain/TutorReplyParserTests.cs:67` e `:78`. Avaliação do teste que o Executor apontou como possivelmente fraco: **o problema se confirma, e atinge também o teste vizinho.**
  - **Malformado.** Em `Parse_WhenJsonIsMalformed_RejectsWithInvalidJson`, a `JsonException` de `{"reply": "Hi?"` já começa com `Expected depth to be zero...`, então `Contain("Expected ")` passa pelo texto da própria exceção.
  - **Texto depois do objeto.** Em `Parse_WhenJsonHasTrailingText_RejectsWithInvalidJson`, a exceção do System.Text.Json para esse caso também traz "Expected ..." (`... invalid after a single JSON value. Expected end of data.`). A asserção da linha 78 fica igualmente vazia.
  - **Efeito.** Se o parser montasse a rejeição `invalid JSON` sem passar pelo catálogo (só `"invalid JSON: " + ex.Message`), os dois testes continuariam verdes, e a instrução de correção da RN-BRAIN04 perderia a descrição do objeto esperado.
  - **Por que não bloqueia.** O `TutorReplyRejectionsTests` garante o sufixo do catálogo com um detalhe que não contém "Expected". O parser chama o catálogo (`TutorReplyParser.cs:48`). A §4 do plano não pedia mais que isso para esses testes, então nenhum teste foi enfraquecido em relação ao plano aprovado.
  - **Esperado.** A rejeição `invalid JSON` que o parser devolve precisa conter a expectativa do catálogo (a forma do objeto JSON esperado), e um teste do parser precisa falhar quando a mensagem trouxer só o texto da `JsonException`. A técnica fica com o Executor, que confirma o Red pelo motivo certo (AP-010).
- **S2** — `tasks/US-002/plan.md` §12, "Executor: escolhas de forma e desvios", desvio AP-009 declarado. Avaliação: **o desvio se confirma, mas o impacto é pequeno.**
  - Duas das quatro escolhas registradas já estavam no plano: §2.2 "remove marcador inicial" e "corta na primeira seta".
  - A fixture `tags-labeled-lines.txt` era conteúdo livre pela §2.3.
  - Só duas decisões são novas e mudam texto observável da mensagem enviada ao modelo:
    - `boolean` para `true`/`false` e nome em minúsculas para os demais `JsonValueKind`, em `invalid corrections` (ea150a5);
    - remoção do ponto final do detalhe em `invalid JSON` (dbe7b8a).
  - As duas cabem no formato da §2.1 (`"<Reason>: <o que está errado>. Expected ..."`) e não contrariam spec nem RN. Por isso a falha fica como sugestão, e não como bloqueante do item "[O] mudanças de comportamento primeiro na spec/plano".
  - Mesmo assim, a regra de `.claude/agents/executor.md:58` (aplicada em 5f4c9c4) foi descumprida: o registro saiu depois dos `feat`, dentro do commit de evidências. Ver "Aprendizados".
- **S3** — `src/EnglishTutor.Core/Brain/TutorReplyRejections.cs:25` e `:37`. Dois comportamentos observáveis da mensagem de rejeição não têm teste. A cobertura de 100% não os pega porque são ternário e `TrimEnd` numa linha só.
  - **Corte do reply.** Um reply com mais de 80 caracteres aparece cortado e com "..." na mensagem `reply must end with a question` (tabela da §2.1).
  - **Ponto final.** Um detalhe de `JsonException` terminado em "." não pode gerar ".." antes de `Expected` (escolha registrada na §12).
  - Esperado: os dois efeitos fixados por teste, para que a US-003 e a US-009 possam mexer no catálogo sem regressão silenciosa.
- **S4** — `src/EnglishTutor.Core/Brain/TutorReplyValidator.cs:34`. A regra 6 da §2.1 diz que `RawText` e `IsRawFallback` "são preservados", mas o validador força `IsRawFallback = false`. Hoje o efeito é nulo, porque os dois caminhos do parser já passam `false`. Esperado: alinhar código e plano, ou preservando o valor recebido, ou registrando no plano que o validador sempre devolve `IsRawFallback = false`. Isso evita surpresa na US-003, que cria a resposta crua.
- **S5** — `src/EnglishTutor.Infrastructure/Brain/TutorReplyParser.cs:157-158`. Há duas linhas em branco seguidas, e o `dotnet format` não acusa. Os auxiliares do caminho JSON (`TryReadCorrections`, `ReadCorrection`, `ReadString`, `KindName`) ficaram depois dos de tags e de `ValidateAndLog`. Agrupar por caminho (JSON e depois tags) deixa a classe mais fácil de ler. Cosmético.

### Pontos positivos

- A separação formato × conteúdo do ADR-005 ficou limpa. O parser só monta o candidato e delega ao `TutorReplyValidator`, e o catálogo `TutorReplyRejections` é a fonte única de motivos e mensagens, pronta para a retentativa da US-003.
- `TutorReplyParseResult` saiu exatamente na forma decidida na §2.1: `IsValid` armazenada, construtor privado, fábricas que validam nulos. O build passa sem CS8775.
- O Red da T7 foi garantido como o plano pedia (B2), com o ramo de tags lançando `NotImplementedException` ao fim da T6. Os 8 métodos da T7 falharam pelo motivo certo, inclusive os de rejeição.
- As evidências da §11 batem número a número com a minha execução, até a cobertura por classe. A observação que o próprio Executor fez sobre a fraqueza do `Contain("Expected ")` facilitou a revisão.
- As mensagens de erro do `Fixture.Read` e do catálogo dizem o que está errado e o que se espera (hábito 5).
- §3 e §5 do plano foram atualizadas com os hashes (AP-005 respeitado).

### Aprendizados para o processo

- **AP-009 se repetiu com a regra já aplicada** (`.claude/agents/executor.md:58`, commit 5f4c9c4). As escolhas de forma foram para a §12 só no commit de evidências. Uma regra escrita no agente não bastou: o momento de registrar não tem um ponto de parada no fluxo. Sugestão:
  - na skill `tdd-dotnet` (ciclo Green) ou no `executor.md`, um passo explícito antes de cada commit `feat`/`fix`: "o Green decidiu algo que o plano deixava em aberto (texto, nome, formato)? Se sim, commit `docs(ai)` na §12 primeiro";
  - na skill `checklist-revisao` §1, um item não obrigatório: "escolhas de forma registradas na §12 têm commit anterior ao `feat` que as implementa".

  Registrar em `docs/aprendizados.md` como recorrência do AP-009, com essa nova correção.
- **Asserção satisfeita pelo texto de terceiros** (S1, padrão novo). `Contain("Expected ")` passa pela mensagem da `JsonException` do System.Text.Json, que também usa "Expected". Sugestão para a skill `tdd-dotnet` e para o checklist do Planejador: quando a mensagem embute texto de biblioteca externa, o teste confere um trecho que só o código do projeto produz, e o plano já mapeia o teste assim na §4. Vale registrar como AP novo.
- **Comportamento de linha única escapa da cobertura de linhas** (S3). O corte em 80 caracteres e o `TrimEnd('.')` estão a 100% sem teste. Sugestão para o checklist de revisão de plano: cada transformação citada nas tabelas de mensagens da §2 (corte, normalização) precisa ter teste na §4, como já se exige para as respostas P*.

Arquivos relevantes:
- C:\Crescimento\TutorIA\tasks\US-002\plan.md
- C:\Crescimento\TutorIA\src\EnglishTutor.Core\Brain\TutorReplyRejections.cs
- C:\Crescimento\TutorIA\src\EnglishTutor.Core\Brain\TutorReplyValidator.cs
- C:\Crescimento\TutorIA\src\EnglishTutor.Infrastructure\Brain\TutorReplyParser.cs
- C:\Crescimento\TutorIA\tests\EnglishTutor.Infrastructure.Tests\Brain\TutorReplyParserTests.cs
- C:\Crescimento\TutorIA\tests\EnglishTutor.Core.Tests\Brain\TutorReplyRejectionsTests.cs
- C:\Crescimento\TutorIA\.claude\agents\executor.md
- C:\Crescimento\TutorIA\docs\aprendizados.md
