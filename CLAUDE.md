# EnglishTutor — regras do projeto

Professor de inglês por voz, local (Whisper + TTS), com "cérebro" plugável (Claude Code headless ou Ollama).
Também é um laboratório de **Spec-Driven Development (SDD)** com 4 agentes.

## Fontes da verdade (leia antes de agir)

- Arquitetura: [ARCHITECTURE.md](ARCHITECTURE.md). Em caso de conflito, ela vence.
- Produto e regras: [specs/PRODUCT.md](specs/PRODUCT.md), [specs/BUSINESS_RULES.md](specs/BUSINESS_RULES.md) (IDs `RN-*`).
- Histórias: `specs/sprints/SPRINT_N.md` (IDs `US-XXX`, critérios em BDD).
- Fases e ambiente: [specs/ROADMAP.md](specs/ROADMAP.md).
- Decisões: `docs/adr/`.

## Fluxo obrigatório (SDD)

Toda funcionalidade é conduzida por `/us US-XXX`:
**Planejador** → aprovação do usuário → **Executor** (TDD) → **Revisor** → correção dos bloqueantes → **Documentador**.

- Nada de código de produção sem US e sem `tasks/US-XXX/plan.md` aprovado.
- Cada cenário BDD vira pelo menos um teste escrito **antes** do código (Red → Green → Refactor).
- Cobertura de linhas do `EnglishTutor.Core` ≥ 80%.
- Mudança de arquitetura exige ADR em `docs/adr/` e atualização do `ARCHITECTURE.md`.

## Regras rápidas

- .NET / C# com `Nullable` e `TreatWarningsAsErrors`; testes com xUnit + FluentAssertions + NSubstitute.
- `EnglishTutor.Core` não referencia pacotes externos (só BCL e `Microsoft.Extensions.*.Abstractions`).
- `CancellationToken` em todo método assíncrono público.
- Commits convencionais com escopo: `feat(core):`, `feat(brain):`, `test(core):`, `docs(ai):`, `chore(infra):`.
- Branches: `main` (estável) ← `develop` (integração) ← `feature/US-XXX_slug`.
- Código, nomes e mensagens de commit em inglês; documentação e specs em português.
- Nunca commitar modelos, bancos SQLite, áudios ou `appsettings.*.local.json`.

## Comandos

```bash
dotnet build
dotnet test
dotnet test --collect:"XPlat Code Coverage"
dotnet run --project src/EnglishTutor.Console
```
