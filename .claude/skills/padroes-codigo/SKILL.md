---
name: padroes-codigo
description: Convenções de código C# deste projeto — nomes, estrutura de pastas, DI, Options, logs, async, erros, commits e branches. Use ao planejar, escrever ou revisar código do EnglishTutor.
user-invocable: false
---

# Padrões de código — EnglishTutor

A estrutura de pastas e as camadas estão em `ARCHITECTURE.md` §2–§3. Aqui ficam as convenções de escrita.

## Projeto e build

- `Directory.Build.props` na raiz: `TargetFramework` (o do `global.json`), `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors=true`, `AnalysisLevel=latest-recommended`, `LangVersion=latest`.
- Versões de pacotes centralizadas em `Directory.Packages.props` (Central Package Management).
- `.editorconfig` da raiz é a referência de estilo; `dotnet format` não pode gerar diff. O build não aplica as regras de estilo (ex.: IDE1006); quem as verifica a cada edição é o hook `post-edit-dotnet` (`dotnet format style --verify-no-changes` no arquivo editado) e, no fim, o D4 (AP-012).

## Nomes

| Elemento | Padrão | Exemplo |
|---|---|---|
| Namespace | file-scoped, espelha a pasta | `namespace EnglishTutor.Core.Conversation;` |
| Interface | `I` + substantivo | `ITutorBrain` |
| Implementação de porta | `<Tecnologia><Porta>` | `ClaudeCodeTutorBrain`, `WhisperNetSpeechToText` |
| Options | `<Área>Options`, seção = nome sem sufixo | `BrainOptions` ↔ `"Brain"` |
| Assíncrono | sufixo `Async`, último parâmetro `CancellationToken ct` | `SendAsync(BrainRequest request, CancellationToken ct)` |
| Campo privado | `_camelCase` | `_brain` |
| Constante privada e campo estático readonly privado | `_camelCase` (também em testes; IDE1006 do `.editorconfig`) | `_headerSize` |
| Testes | `Metodo_Cenario_Resultado` | ver skill `tdd-dotnet` |

Caracteres não imprimíveis ou invisíveis (BOM, espaço sem quebra, zero-width etc.) aparecem em código e testes **sempre como escape** (`'\uXXXX'`), nunca como caractere literal (AP-007).

Código, identificadores, logs e mensagens de commit em **inglês**. Mensagens exibidas ao aluno em inglês (ele está praticando), exceto quando a spec disser outra coisa.

## Tipos

- DTOs e valores de domínio: `sealed record`. Coleções expostas como `IReadOnlyList<T>`.
- Classes `sealed` por padrão. Construtores primários para injeção.
- Sem `static` mutável; sem service locator.
- `TimeProvider` injetado em vez de `DateTime.Now` (testável).

## DI e configuração

- Cada projeto expõe uma extensão de registro: `services.AddEnglishTutorCore()`, `services.AddEnglishTutorInfrastructure(promptsDirectory)`. Cada uma registra os validadores das próprias Options; o bind das seções fica no composition root (ADR-004).
- Options: `services.AddOptions<BrainOptions>().Bind(config.GetSection(BrainOptions.SectionName)).ValidateOnStart();` no composition root + um `IValidateOptions<T>` por classe de Options, com mensagens que dizem o que está errado e o que se espera.
- Com mais de uma classe de Options validada na partida, falhas simultâneas de seções diferentes chegam como `AggregateException`; a mensagem de configuração inválida cobre todas elas, com teste de duas seções inválidas ao mesmo tempo (AP-039).
- Nada lê `IConfiguration` diretamente fora do composition root.

## Logs (Serilog via `ILogger<T>`)

- Templates estruturados, nunca interpolação: `_logger.LogInformation("Brain {Provider} replied in {ElapsedMs} ms", ...)`.
- Medir latência por etapa (`Stopwatch`/`TimeProvider`) e logar como propriedade.
- Nunca logar o conteúdo completo do system prompt; texto do aluno só em nível `Debug`.

## Erros

- Falhas de provedores viram exceções de domínio (`BrainException` com `BrainErrorKind`), traduzidas pelo adaptador; o Core não conhece `Win32Exception` nem `HttpRequestException`.
- Sem `catch (Exception)` silencioso; se capturar genérico, logue e relance ou converta.
- `OperationCanceledException` sempre propaga.
- Validação restrita de frase do modelo (ex.: exatamente 1 `?`): o prompt e a `RN-*` mantêm a regra original, mas o verificador aceita um nível acima (1 ou 2 `?`). Convenção "Tolerância de verificação" em `specs/BUSINESS_RULES.md`; registre "Regra pedida" × "Tolerância" na `RN-*`. Não vale para JSON, tags ou segurança.

## Console e entrada padrão

- A entrada padrão redirecionada pode chegar com BOM UTF-8 (o Windows PowerShell 5.1 coloca `\uFEFF` antes do texto enviado por pipe). Remova o BOM inicial da primeira linha lida e tenha um teste para isso (US-001, P12; AP-006).
- Na DoD de toda US que mexe no console, mantenha ao menos um comando com pipe executado no Windows PowerShell 5.1 (ex.: `"/quit" | dotnet run --project src/EnglishTutor.Console`).

## Processos externos

- Envolva `System.Diagnostics.Process` numa porta (`IProcessRunner`) — testável com fake.
- `ArgumentList` (nunca concatenar string de argumentos); conteúdo grande via stdin; ler stdout/stderr de forma assíncrona; matar a árvore do processo no timeout (`Kill(entireProcessTree: true)`).
- O processo filho herda o ambiente de quem abre o app. O `IProcessRunner` deixa explícito quais variáveis de ambiente o filho recebe, e a DoD que usa processo real diz em que ambiente roda (o comando limpa o que for preciso). Limpar o ambiente como regra do runner é decisão de ADR (AP-034).

## Git

- Branch: `feature/US-XXX_slug-curto` a partir de `develop`, com o nome do que a US cria (ex.: `feature/US-001_fundacao-solucao`). US pronta → merge `--no-ff` em `develop`. `main` só recebe `develop` depois de testado, a pedido do usuário.
- Commits convencionais: `<tipo>(<escopo>): <imperativo em inglês>`; tipos `feat`, `fix`, `test`, `refactor`, `docs`, `chore`, `ci`, `perf`. Escopo `ai` para governança/agentes (`docs(ai): ...`).
- Commits pequenos: um ciclo TDD por commit (ou um par test/feat).
- Mensagem de commit com várias linhas ou aspas: `git commit -F -` com heredoc no Bash; não use here-string do Windows PowerShell 5.1 (AP-021).
