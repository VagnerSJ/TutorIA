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
- `.editorconfig` da raiz é a referência de estilo; `dotnet format` não pode gerar diff.

## Nomes

| Elemento | Padrão | Exemplo |
|---|---|---|
| Namespace | file-scoped, espelha a pasta | `namespace EnglishTutor.Core.Conversation;` |
| Interface | `I` + substantivo | `ITutorBrain` |
| Implementação de porta | `<Tecnologia><Porta>` | `ClaudeCodeTutorBrain`, `WhisperNetSpeechToText` |
| Options | `<Área>Options`, seção = nome sem sufixo | `BrainOptions` ↔ `"Brain"` |
| Assíncrono | sufixo `Async`, último parâmetro `CancellationToken ct` | `SendAsync(BrainRequest request, CancellationToken ct)` |
| Campo privado | `_camelCase` | `_brain` |
| Testes | `Metodo_Cenario_Resultado` | ver skill `tdd-dotnet` |

Código, identificadores, logs e mensagens de commit em **inglês**. Mensagens exibidas ao aluno em inglês (ele está praticando), exceto quando a spec disser outra coisa.

## Tipos

- DTOs e valores de domínio: `sealed record`. Coleções expostas como `IReadOnlyList<T>`.
- Classes `sealed` por padrão. Construtores primários para injeção.
- Sem `static` mutável; sem service locator.
- `TimeProvider` injetado em vez de `DateTime.Now` (testável).

## DI e configuração

- Cada projeto expõe uma extensão de registro: `services.AddEnglishTutorCore()`, `services.AddEnglishTutorInfrastructure(configuration)`.
- Options: `services.AddOptions<BrainOptions>().Bind(config.GetSection("Brain")).ValidateDataAnnotations().ValidateOnStart();` + `IValidateOptions<T>` quando a validação for condicional.
- Nada lê `IConfiguration` diretamente fora do composition root.

## Logs (Serilog via `ILogger<T>`)

- Templates estruturados, nunca interpolação: `_logger.LogInformation("Brain {Provider} replied in {ElapsedMs} ms", ...)`.
- Medir latência por etapa (`Stopwatch`/`TimeProvider`) e logar como propriedade.
- Nunca logar o conteúdo completo do system prompt; texto do aluno só em nível `Debug`.

## Erros

- Falhas de provedores viram exceções de domínio (`BrainException` com `BrainErrorKind`), traduzidas pelo adaptador; o Core não conhece `Win32Exception` nem `HttpRequestException`.
- Sem `catch (Exception)` silencioso; se capturar genérico, logue e relance ou converta.
- `OperationCanceledException` sempre propaga.

## Processos externos

- Envolva `System.Diagnostics.Process` numa porta (`IProcessRunner`) — testável com fake.
- `ArgumentList` (nunca concatenar string de argumentos); conteúdo grande via stdin; ler stdout/stderr de forma assíncrona; matar a árvore do processo no timeout (`Kill(entireProcessTree: true)`).

## Git

- Branch: `feature/US-XXX_slug-curto` a partir de `develop`.
- Commits convencionais: `<tipo>(<escopo>): <imperativo em inglês>`; tipos `feat`, `fix`, `test`, `refactor`, `docs`, `chore`, `ci`, `perf`. Escopo `ai` para governança/agentes (`docs(ai): ...`).
- Commits pequenos: um ciclo TDD por commit (ou um par test/feat).
