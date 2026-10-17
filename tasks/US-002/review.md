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

## Rodada 2

Data: 2026-10-17
Commits revisados: 053779d..d1c9128 (pedido do usuário em 8ab0b69; rodada 1 cobriu d5694e5..a7ecdd0)
Resultado: APROVADO

### Verificações
| Item | Resultado |
|---|---|
| Build sem warnings | ✅ (`dotnet build --no-incremental`: 0 avisos, 0 erros) |
| Testes (exceto LiveBrain) | ✅ 97 passaram (Core.Tests 51, Infrastructure.Tests 24, Console.Tests 22) |
| Cobertura de linhas do Core | 100% (Infrastructure 81,6%, informativo; total 86,2%) |
| Cenários BDD com teste | 6/6 (sem mudança em relação à rodada 1) |
| Teste antes do código (ordem de commits) | ✅ |
| Evidências da DoD conferem | ✅ |
| Intenção antes do código (spec/ADR → código) | ✅ (escolhas da T9 registradas na §12 e na §2.1 regra 6 em 053779d, antes de f3c8b36) |

#### Detalhe das verificações

- **Escopo da rodada.** O diff desde 8ab0b69 mexe em 5 arquivos: 1 linha removida em `TutorReplyValidator.cs`, testes novos ou reforçados em 3 arquivos de teste e o `plan.md` (§2.1 regra 6, §3, §4, §5 T9, §10 D2/D6, §11, §12). Nenhum código de produção mudou além da S4. S2 e S5 não foram tocadas, como o usuário pediu.
- **Ordem.** O registro das escolhas (053779d, 15:43) vem antes de todos os testes. Depois vêm S1 com teste só (f3c8b36), S3 com teste só (e8f5f00), o Red da S4 (4a5df2b) seguido do fix (f17a5ed) e, por último, as evidências (d1c9128). Desta vez o AP-009 foi respeitado.
- **Os testes novos falhariam sem o comportamento que fixam?** Fiz a análise lendo o código, sem rodar mutações, porque não edito arquivos.
  - **S1.** A mutação `"invalid JSON: " + ex.Message` elimina o trecho `Expected a single JSON object {"reply": string` e o final `with no text around it.`. Esses dois trechos só existem em `TutorReplyRejections.cs:37-39`. Os dois testes do parser falham com ela.
  - **S3, corte.**
    - O reply de 81 caracteres termina o 80º caractere em `b`. Por isso, um corte em 79 também quebraria o `Contain`, e não só a ausência de corte.
    - Trocar `>` por `>=` faz o teste dos 80 caracteres falhar no `NotContain("...")`. O texto fixo da mensagem não tem `...`, então essa asserção discrimina.
  - **S3, ponto final.** Sem `TrimEnd('.')`, a mensagem traz `data.. Expected`, e tanto o `Contain` quanto o `NotContain("..")` falham. O catálogo não tem `..` em nenhum outro lugar.
  - **S4.** Com a linha `IsRawFallback = false` de volta, o `with` força `false` e `result.Reply!.IsRawFallback.Should().BeTrue()` falha. Isso bate com o Red real colado na T9.
- **Contagens.** A §4 fala em 35 métodos e 48 casos (31+4 e 44+4). D2 dá 97, igual ao que obtive. D5 dá 24/24, igual. D6 dá 7/7: com o filtro `FullyQualifiedName~TutorReplyRejectionsTests`, obtive 7 casos só no Core.Tests (4 do `[Theory]` + 3 `[Fact]` da S3).
- **Documentação alinhada.** A §2.1 regra 6 agora diz "preservados como vieram no candidato", o que bate com o código. A `ARCHITECTURE.md` (linhas 100 e 130) e a `RN-BRAIN04` não afirmavam que o validador força `false`, então não há outra fonte a atualizar.
- **Qualidade e segurança.** A rodada não trouxe `catch` novo, I/O, pacote ou configuração. O comentário novo em `TutorReplyParserTests.cs:11` está em inglês, como pede a regra de código.
- **Caracteres invisíveis.** `git diff develop...HEAD | LC_ALL=C grep -c ...` imprime `0`.
- **Git.** Os commits são convencionais, com escopo (`test(infra)`, `test(core)`, `fix(core)`, `docs(ai)`). A T9 está marcada `[x]` com os hashes, e a §3 foi atualizada (AP-005).

#### Evidências (§11, rodada 2 sobre f17a5ed) × o que eu obtive

| # | Evidência do Executor | Minha execução |
|---|---|---|
| D1 | 0 avisos, 0 erros | Igual |
| D2 | 97 verdes (51/24/22) | Igual |
| D3/D3b | Core 100%, Infrastructure 81,6%, total 86,2% | Igual. Todas as classes de `EnglishTutor.Core.Brain` a 100%. Gerei o `reportgenerator` e os resultados em `%TEMP%\us002r2`, fora do repositório |
| D4 | Sem diferenças | Igual (código de saída 0) |
| D5 | 24/24 | Igual |
| D6 | 7/7, mensagens sem mudança de texto | Igual. Nenhuma linha de mensagem do catálogo mudou no diff |
| D7 (testes) | 3/3 | Não rodei de novo isoladamente. Está incluído no D2 verde, e o diff não toca os `.csproj` |
| D7/D8 (`dotnet list package`), D9 (`dotnet sln list`) | Conforme §11 | Comando fora da minha lista permitida: **evidência do Executor, não reproduzida**. O diff da rodada não altera `.csproj` nem `EnglishTutor.slnx`, então isso é coerente com a rodada 1 |
| D10 | `git status` vazio | Igual |
| D11a/b | `0` / sem saída | Igual (D11a) |

### Itens da rodada anterior

- [x] **S1** — resolvida em f3c8b36. Os dois testes `invalid JSON` do parser conferem `Expected a single JSON object {"reply": string` e `EndWith("with no text around it.")`. Esses trechos só existem no catálogo, então os testes falham se a mensagem trouxer apenas o texto da `JsonException`.
- [ ] **S2** — não pedida pelo usuário (desvio aceito na §12). Não reaberta.
- [x] **S3** — resolvida em e8f5f00. Três testes em `TutorReplyRejectionsTests` fixam:
  - o corte em 80 caracteres + `...`;
  - o limite (80 caracteres citados inteiros);
  - a ausência de `..` antes de `Expected`.
- [x] **S4** — resolvida em 4a5df2b (Red real) + f17a5ed. O validador preserva `IsRawFallback` e `RawText` do candidato, e a §2.1 regra 6 foi reescrita em 053779d.
- [ ] **S5** — não pedida pelo usuário. Não reaberta.

### Bloqueantes
Nenhum.

### Sugestões

- **S6** — `tests/EnglishTutor.Core.Tests/Brain/TutorReplyValidatorTests.cs:136`. O teste novo da S4 troca o `RawText` padrão (`"raw text"`, diferente do reply) por um valor igual ao `Reply` (`"What did you do?"`). Com isso, a asserção de `RawText` desse teste não distinguiria um validador que copiasse o reply normalizado para `RawText`. Hoje não há risco: `Validate_WhenNoCorrectionsAndNoPraise_IsValidWithoutWarnings` (linha 75) já fixa `RawText == "raw text"`. Esperado, quando alguém mexer nesse teste: que a preservação do texto cru seja conferida com um valor que não coincida com o reply. Cosmético, e não precisa ser aplicada nesta US.

### Pontos positivos

- **AP-009 respeitado nesta rodada.** As escolhas de forma (trecho exclusivo do catálogo, limite "até 80", reescrita da regra 6) foram para a §12 e a §2.1 num `docs(ai)` antes do primeiro teste. É a correção que a rodada 1 pediu.
- **Testes que só fixam comportamento existente com Red declarado.** S1 e S3 não precisavam de código novo, e o Executor mostrou o Red por mutação temporária não commitada, descrevendo cada mutação na T9. As asserções discriminam de verdade: o `b` no 80º caractere separa um corte em 79 de um corte em 80, e o `NotContain("...")` pega a troca de `>` por `>=`.
- O comentário da constante `_catalogJsonExpectation` explica por que o trecho foi escolhido (a `JsonException` também diz "Expected"). Isso evita que alguém volte a enfraquecer a asserção.
- As evidências da §11 foram refeitas inteiras sobre f17a5ed e dizem o que mudou em relação à rodada 1 (D2/D3 de 93 para 97, D6 de 4 para 7). Todas as contagens batem com a minha execução.

### Aprendizados para o processo

- **O registro na §12 antes do código funcionou** quando o pedido do usuário já listava as mudanças, como foi o caso da T9. Isso reforça a sugestão da rodada 1: um ponto de parada explícito antes de cada `feat`/`fix` (na `tdd-dotnet` ou no `executor.md`) cobre também o caso em que a escolha surge no meio do Green, sem um pedido prévio do usuário. Sugiro registrar em `docs/aprendizados.md`, junto da recorrência do AP-009, que a T9 seguiu a regra.
- **Red por mutação para testes que fixam comportamento já existente** (S1 e S3). O padrão que funcionou foi: mutação temporária, não commitada, descrita no plano com a mensagem de falha obtida. Sugiro incluir esse procedimento na skill `tdd-dotnet`, para testes de caracterização pedidos em revisão, ligado ao AP-010 (o Red precisa falhar pelo motivo certo).
- **Valor de teste que coincide com outro campo** (S6, padrão leve). Quando um teste confere que um campo é preservado, o valor escolhido não deve coincidir com o de outro campo que o código poderia copiar por engano. Pode virar uma linha na `tdd-dotnet`, sem AP novo.

Arquivos relevantes:
- C:\Crescimento\TutorIA\tasks\US-002\plan.md
- C:\Crescimento\TutorIA\tasks\US-002\review.md
- C:\Crescimento\TutorIA\src\EnglishTutor.Core\Brain\TutorReplyValidator.cs
- C:\Crescimento\TutorIA\src\EnglishTutor.Core\Brain\TutorReplyRejections.cs
- C:\Crescimento\TutorIA\tests\EnglishTutor.Core.Tests\Brain\TutorReplyRejectionsTests.cs
- C:\Crescimento\TutorIA\tests\EnglishTutor.Core.Tests\Brain\TutorReplyValidatorTests.cs
- C:\Crescimento\TutorIA\tests\EnglishTutor.Infrastructure.Tests\Brain\TutorReplyParserTests.cs
