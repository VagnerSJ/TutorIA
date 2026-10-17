# ADR-004 — Fundação da solução: Generic Host, configuração validada no Core, logs só em arquivo e testes do console em processo

- **Status:** Aceito (2026-10-17)
- **Data:** 2026-10-17
- **US relacionadas:** US-001 (base para todas as seguintes)

## Contexto

A US-001 cria a solução .NET e define como as próximas US vão acrescentar comportamento sem retrabalho. Precisamos decidir, antes do código:

- formato da solução e gestão de versões de pacotes;
- onde mora a validação da configuração do cérebro (`Brain:Provider`), dado que o Core só pode depender de BCL e `Microsoft.Extensions.*.Abstractions` / `Microsoft.Extensions.Options` (ARCHITECTURE R1);
- como testar o comportamento do console (cabeçalho, `/quit`, comando desconhecido, código de saída, log) **sem processo real** e em segundos (ARCHITECTURE §10, hábito 5);
- para onde vão os logs, sem poluir a tela de conversa do aluno;
- pilha de testes compatível com `--filter "Category!=LiveBrain"` e `--collect:"XPlat Code Coverage"`.

## Decisão

1. **Solução** `EnglishTutor.slnx` (formato XML novo, já suportado pelos hooks), `global.json` fixando o SDK .NET 10 (`rollForward: latestFeature`), `Directory.Build.props` com `Nullable`, `ImplicitUsings`, `TreatWarningsAsErrors`, `AnalysisLevel=latest-recommended`, e **Central Package Management** (`Directory.Packages.props`).
2. **Configuração do cérebro no Core**: `BrainOptions` (seção `Brain`) e `BrainOptionsValidator : IValidateOptions<BrainOptions>` ficam em `EnglishTutor.Core/Brain`, junto com a lista canônica de provedores aceitos. O *binding* (`Bind(config.GetSection("Brain"))` + `ValidateOnStart`) fica no composition root (Console), porque exige `Microsoft.Extensions.Options.ConfigurationExtensions`, que o Core não pode referenciar.
3. **Composition root** com `Microsoft.Extensions.Hosting` (`HostApplicationBuilder` com `DisableDefaults` e fontes explícitas: `appsettings.json`, `appsettings.{Environment}.json`, variáveis de ambiente, linha de comando). O ponto de entrada é `EnglishTutorApp.RunAsync(AppRunOptions, CancellationToken)`, que recebe `TextReader`/`TextWriter` e uma configuração opcional de teste; `Program.cs` só repassa `Console.In/Out/Error`. Testes chamam `RunAsync` em processo, sem variáveis de ambiente da máquina.
4. **Logs**: Serilog configurado por `ReadFrom.Configuration` (seção `Serilog`), **somente sink de arquivo** em `logs/englishtutor-.log` (rolagem diária, formato estruturado CLEF/JSON compacto). O console é exclusivo da interface do aluno. Eventos de aplicação usam `[LoggerMessage]` com `EventId` nomeado (ex.: `ApplicationStarted`, `ApplicationStopped`). Logger por instância (`AddSerilog(logger, dispose: true)`), sem `Log.Logger` estático.
5. **Testes**: xUnit v3 sobre VSTest (`xunit.runner.visualstudio` + `Microsoft.NET.Test.Sdk` + `coverlet.collector`), FluentAssertions **7.x** (Apache 2.0) e NSubstitute. Novo projeto `tests/EnglishTutor.Console.Tests` para o comportamento do console (casa com o mapeamento `src/X → tests/X.Tests` do hook). Teste de arquitetura do Core por reflexão (`GetReferencedAssemblies`) e por leitura do `.csproj`.
6. **Comandos locais** (RN-CONV06) são regra de negócio: o reconhecimento (`LocalCommandParser`, `LocalCommand`, lista de comandos disponíveis) mora em `EnglishTutor.Core/Conversation`; o console só reage ao resultado.

## Alternativas consideradas

| Alternativa | Prós | Contras |
|---|---|---|
| `.sln` clássico | Universal | Verboso, conflitos de merge; `.slnx` já é suportado pelo SDK 10 e pelos hooks |
| `Provider` como `enum` | Tipado | O binder lança exceção genérica para valor inválido; a mensagem não lista os valores aceitos (exigência do cenário) |
| Validação com DataAnnotations (`[AllowedValues]`) | Menos código | Mensagem menos controlável; a validação condicional das próximas US (subseções por provedor) pede `IValidateOptions` |
| Sinks de console + arquivo (texto atual da ARCHITECTURE §9) | Visibilidade imediata | Mistura log com a conversa do aluno e duplica a mensagem de erro de configuração |
| Testar o console executando o processo (`dotnet run`) | Fidelidade total | Lento (segundos por teste), depende de build e de ambiente; fica só na DoD (D5–D7) |
| FluentAssertions 8.x | Versão mais nova | Licença comercial (Xceed) |
| Microsoft.Testing.Platform como runner | Mais rápido | `--collect:"XPlat Code Coverage"` e `--filter` da DoD supõem VSTest; migração pode virar ADR futuro |

## Consequências

- Positivas: próximas US só registram serviços e acrescentam comandos/handlers; comportamento do console testável em milissegundos; regra de provedores aceitos num único lugar.
- Negativas / riscos: `ARCHITECTURE.md` precisa de ajustes no mesmo PR (§2 tabela de dependências do Core inclui `Microsoft.Extensions.DependencyInjection.Abstractions`; §3 inclui `tests/EnglishTutor.Console.Tests` e `Conversation/LocalCommand*`; §9 logs só em arquivo). xUnit v3 exige passar `TestContext.Current.CancellationToken` (analisador xUnit1051 vira erro com `TreatWarningsAsErrors`).
- O que precisa ser medido ou revisitado: tempo da suíte (< 30 s); quando entrar a UI WPF/Blazor, se o sink de console volta a fazer sentido; migração para Microsoft.Testing.Platform.
