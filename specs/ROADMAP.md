# Roadmap — fases, ambiente e estimativas

## 1. Ambiente verificado (2026-10-17)

| Item | Encontrado | Ação |
|---|---|---|
| SO | Windows 11 Pro 10.0.26200 | — |
| GPU | NVIDIA GeForce RTX 4070 Ti SUPER, 16 GB, driver 610.88 | Usar runtime **CUDA** do Whisper.net |
| CUDA Toolkit | não instalado (`nvcc` ausente, `CUDA_PATH` vazio) | Necessário para `Whisper.net.Runtime.Cuda` (Fase 4). Alternativa sem instalação: runtime Vulkan |
| .NET SDK | apenas 9.0.200 (runtimes 6, 8, 9) | **Instalar .NET 10 SDK (LTS)** antes do Executor da US-001 |
| Claude Code CLI | só o binário embutido no app desktop; não está no PATH; `claude auth status` → não autenticado | Instalar o CLI e autenticar com a assinatura |
| Ollama | 0.40.7 instalado (2026-10-21); modelo `qwen2.5:14b-instruct-q4_K_M` baixado (9,0 GB) | — (US-006) |
| Python | não instalado | Não necessário se usarmos Piper (Fase 3) |
| Node.js | 22.16.0 | — |
| Git | 2.56 | — |
| GitHub CLI | não instalado | Opcional (PRs/Actions) |

### Comandos de instalação

```powershell
winget install Microsoft.DotNet.SDK.10
irm https://claude.ai/install.ps1 | iex      # Claude Code CLI nativo
claude auth login                             # entrar com a assinatura (não usar --console)
winget install Ollama.Ollama                  # Fase 2b
winget install Nvidia.CUDA                    # Fase 4 (Whisper CUDA)
winget install GitHub.cli                     # opcional
```

Depois de instalar, abra um terminal novo e confira: `dotnet --list-sdks`, `claude --version`, `claude auth status --text`, `ollama --version`.

## 2. Descobertas na documentação do Claude Code (v2.1.299)

- `--system-prompt-file` existe e **substitui** o prompt padrão; pode ser combinado com `--append-system-prompt-file`.
- `--tools ""` remove todas as ferramentas embutidas; MCP precisa de `--disallowedTools "mcp__*"` + `--strict-mcp-config`.
- `--permission-mode dontAsk` nega tudo que pediria permissão (ideal para o professor).
- `--json-schema` + `--output-format json` → resultado validado no campo `structured_output`.
- `--bare` exige `ANTHROPIC_API_KEY` (não usa assinatura) — **não usar**.
- Com `--resume`, o system prompt é gravado no primeiro turno e reutilizado (`--system-prompt-snapshot`), então mudanças no `tutor.md` só valem em sessão nova.
- Subagentes: `.claude/agents/*.md` com frontmatter `name`, `description`, `tools`, `disallowedTools`, `model`, `skills` (pré-carrega skills).
- Skills: `.claude/skills/<nome>/SKILL.md`; viram `/nome`; `disable-model-invocation: true` para fluxos com efeitos colaterais.

**Recomendação sobre o formato de saída:** usar `--json-schema` (Claude Code) e `format` com o mesmo schema (Ollama) como caminho principal, com o parser de tags como alternativa. Detalhes e riscos no [ADR-002](../docs/adr/ADR-002-contrato-saida-estruturada.md). A latência das duas opções será medida na US-004.

## 3. Fases

Estimativa em sessões de trabalho de ~2 h, já incluindo o fluxo completo de agentes por US.

| Fase | Conteúdo | US | Estimativa |
|---|---|---|---|
| 0 | Governança e agentes: `CLAUDE.md`, `ARCHITECTURE.md`, specs, Sprint 1 com DoD verificável, subagentes, skills (incl. `revisar-contexto`), `/us`, hooks de build/teste, `docs/aprendizados.md`, ADRs. Piloto do fluxo na US-001 | — | 1–2 |
| 1 | Fundação: solução, DI, Options, Serilog, CI; contrato do professor; `ConversationService` dono do histórico | US-001, US-002, US-003 | 3–4 |
| 2 | Cérebro Claude Code (processo por chamada), erros e mensagens claras, chat por texto no console | US-004, US-005 | 2–3 |
| 2b | Ollama, `BrainFactory` + fallback, testes de contrato, golden set + `eval` | US-006 a US-009 | 3–4 |
| 3 | Voz de saída: Piper (fala só o reply), velocidade configurável; Kokoro como alternativa | Sprint 2 | 2 |
| 4 | Voz de entrada: push-to-talk (NAudio) + Whisper.net CUDA com confiança por palavra; ciclo completo | Sprint 2 | 3 |
| 5 | Persistência SQLite, histórico, resumo de erros recorrentes no início da sessão | Sprint 3 | 2–3 |
| 6 | Latência: `ClaudeCodeStreamingBrain` (stream-json persistente), medição comparativa, streaming de TTS | Sprint 3 | 2–3 |
| 7 | Modos (conversa, entrevista, escuta), velocidade, relatório de sessão | Sprint 4 | 2 |
| 8 | Futuro: WPF/Blazor, pronúncia por fonemas (wav2vec2), provedor API Anthropic | — | — |

## 4. Riscos conhecidos

- **Latência do Claude Code por processo**: cada `claude -p` tem custo de inicialização (segundos). Mitigação: modelo rápido (`haiku`), Fase 6 com processo persistente.
- **`--json-schema` pode adicionar latência** (a saída estruturada é produzida no fim do ciclo do agente). Medir contra tags na US-004.
- **Limites de uso da assinatura**: fallback automático para Ollama (US-007).
- **CUDA no Windows**: Whisper.net CUDA depende de versão compatível do CUDA Toolkit; manter Vulkan/CPU como fallback.
- **VRAM**: Whisper large + LLM 14B pode passar de 13 GB; permitir Whisper menor quando o LLM local estiver ativo.
