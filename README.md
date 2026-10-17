# EnglishTutor 🎙️

Professor de inglês **por voz**, pessoal e local, para treinar fala e escuta: conversação do dia a dia, vocabulário de desenvolvimento de software e simulação de entrevistas técnicas.

O áudio fica na máquina: fala → texto com Whisper, texto → fala com Piper ou Kokoro. O "cérebro" é plugável: **Claude Code headless** (usa a assinatura, sem API key) ou um **modelo local via Ollama** na GPU.

> 🚧 **Status:** Fase 0 concluída (governança e agentes). Sprint 1 em andamento — ver [specs/sprints/SPRINT_1.md](specs/sprints/SPRINT_1.md) e [specs/ROADMAP.md](specs/ROADMAP.md).

## Arquitetura

```mermaid
flowchart LR
    Mic[🎤 Microfone] -->|push-to-talk| STT[Whisper.net<br/>texto + confiança]
    STT --> CS[ConversationService<br/>dono do histórico]
    CS --> Brain{ITutorBrain}
    Brain -->|ClaudeCode| CC[claude -p]
    Brain -->|Ollama| OL[LLM local]
    Brain --> CS
    CS -->|só o reply| TTS[Piper / Kokoro] --> Spk[🔊]
    CS -->|feedback| UI[Tela]
    CS --> DB[(SQLite)]
```

Detalhes em [ARCHITECTURE.md](ARCHITECTURE.md). Decisões em [docs/adr/](docs/adr/).

## Como o projeto é desenvolvido

Spec-Driven Development com 4 agentes nativos do Claude Code ([ADR-003](docs/adr/ADR-003-fluxo-sdd-com-agentes.md)):

```mermaid
flowchart LR
    US[US em specs/sprints<br/>cenários BDD] --> P[planejador<br/>tasks/US/plan.md]
    P --> H{aprovação<br/>humana}
    H --> E[executor<br/>TDD + commits]
    E --> R[revisor<br/>tasks/US/review.md]
    R -->|bloqueantes| E
    R -->|aprovado| D[documentador<br/>docs/us, ADRs, README]
```

Tudo é conduzido por `/us US-XXX` dentro do Claude Code. Veja [`.claude/`](.claude/) e [CLAUDE.md](CLAUDE.md).

## Pré-requisitos

| Item | Para quê | Instalação |
|---|---|---|
| .NET 10 SDK | build e execução | `winget install Microsoft.DotNet.SDK.10` |
| Claude Code CLI (autenticado) | provedor `ClaudeCode` | `irm https://claude.ai/install.ps1 \| iex` e depois `claude auth login` |
| Ollama | provedor local | `winget install Ollama.Ollama` |
| GPU NVIDIA + CUDA Toolkit | Whisper na GPU | `winget install Nvidia.CUDA` (Vulkan e CPU como alternativa) |

## Como rodar

> Disponível a partir da US-001.

```bash
dotnet build
dotnet test --filter "Category!=LiveBrain"
dotnet run --project src/EnglishTutor.Console
```

## Estrutura

```
.claude/        agentes e skills do fluxo SDD
specs/          produto, regras de negócio (RN-*), sprints (US-*), roadmap
tasks/          planos e revisões por US
docs/           ADRs e documentação por US
prompts/        system prompt do professor e instruções de formato
src/ tests/     código (a partir da US-001)
eval/           golden set para comparar cérebros
```

## Licença

A definir.
