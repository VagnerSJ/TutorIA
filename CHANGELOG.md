# Changelog

Formato baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/); versionamento [SemVer](https://semver.org/lang/pt-BR/).

## [Unreleased]

### Added
- Estrutura de governança SDD: `CLAUDE.md`, `ARCHITECTURE.md`, specs (produto, regras de negócio, Sprint 1, roadmap), subagentes `planejador`, `executor`, `revisor`, `documentador`, skills de apoio e orquestrador `/us` (Fase 0).
- ADR-001 (Claude Code headless como cérebro), ADR-002 (contrato de saída estruturada), ADR-003 (fluxo SDD com agentes).
- System prompt do professor (`prompts/tutor.md`) e instruções de formato JSON e tags.
- Added: solução .NET 10 em camadas (`Core`, `Infrastructure`, `Console` e testes), com `global.json`, `Directory.Build.props`, Central Package Management e CI no GitHub Actions (US-001).
- Added: configuração `Brain:Provider` validada na inicialização, com mensagem que lista os provedores aceitos e código de saída 2 (US-001).
- Added: casca de console com cabeçalho de estado e comandos locais `/help`, `/quit` e desconhecido (RN-CONV06), códigos de saída 0/1/2 e Ctrl+C limpo (US-001).
- Added: logs estruturados (Serilog, CLEF) em `logs/englishtutor-.log` (US-001).
- Added: contrato `TutorReply`/`Correction` no Core e `TutorReplyParser` único na Infrastructure, que aceita o JSON canônico e o formato com tags (US-002).
- Added: rejeição de respostas inválidas com motivo curto e mensagem que diz o que faltou e o que se espera, base da retentativa (RN-BRAIN04) (US-002).
- Added: validação de conteúdo no Core (`TutorReplyValidator`: reply com pergunta final, no máximo 2 correções) e `FeedbackFormatter` com as linhas de feedback (US-002).
- Added: projeto `EnglishTutor.Infrastructure.Tests` com fixtures de exemplo da resposta do professor; ADR-005 (regras de conteúdo no Core, formato na Infrastructure) (US-002).
- Added: contrato do cérebro no Core (`ITutorBrain`, `BrainRequest`, `BrainMessage`, `BrainException` com `BrainErrorKind`) e `TutorReply.RawFallback` (US-003).
- Added: `ConversationService` dono do histórico: mensagem padrão do turno (RN-CONV02), janela dos últimos N turnos (`Conversation:MaxTurnsInWindow`, padrão 10, mínimo 1), troca de modo, retentativa única de formato e fallback para texto cru (RN-BRAIN04) (US-003).
- Added: `/mode` reconhecido pelo parser de comandos e `TutorMode.TryParse`; o console ainda o trata como desconhecido até a US-004 (US-003).
- Added: console mostra o aviso local `Nothing was sent: type a sentence in English and press Enter.` em linha em branco (RN-CONV05) (US-003).
- Added: ADR-006 (retentativa de formato e histórico no `ConversationService`) (US-003).
- Added: `ClaudeCodeTutorBrain`, primeiro cérebro real: um processo `claude -p` isolado por turno (sem `--bare`, sem ferramentas, pasta de trabalho dedicada), histórico controlado pela aplicação e `--resume` como otimização (US-004).
- Added: console conversa por texto com o professor: mostra reply e feedback, `/mode` troca o modo e `/help` o lista (US-004).
- Added: configuração `Brain:ClaudeCode:*` (`ExecutablePath`, `Model`, `WorkingDirectory`, `UseResume`, `OutputMode`) e bind de `Conversation:MaxTurnsInWindow` com validação na inicialização; várias seções inválidas ao mesmo tempo continuam encerrando com código 2 (US-004).
- Added: ADR-007 (processo por turno, histórico e `--resume`) e medição de latência JsonSchema × Tags no ADR-002 (US-004).
- Changed: RN-FEED01: o `reply` deve ter exatamente uma pergunta em qualquer posição, e não mais terminar com ela; a recusa diz quantas perguntas encontrou (US-004).
- Changed: o console passa a emitir UTF-8 sem BOM, para a seta `→` do feedback sair legível no Windows (US-004).
- Added: falhas do Claude Code classificadas em `CliNotFound`, `NotAuthenticated`, `UsageLimitReached`, `Timeout` e `Unknown`, com mensagem que diz o que houve e o que fazer (US-005).
- Added: `Brain:ClaudeCode:TimeoutSeconds` (padrão 60, faixa 1 a 3600) limita cada chamada ao `claude -p` e encerra o processo e seus filhos ao estourar (US-005).
- Added: ADR-008 (classificação de falhas e tempo máximo por chamada do Claude Code) e fixtures de erro gravadas e sintéticas (US-005).
- Changed: falha do cérebro no console não encerra mais a sessão: a mensagem vai para a saída de erro, é registrada como aviso (evento 1005) e o prompt volta (US-005).
- Fixed: `/quit` enviado por pipe no Windows PowerShell 5.1 era lido com BOM UTF-8 e tratado como texto livre (US-001).
- Hábitos de trabalho com IA: skill `revisar-contexto`, `docs/aprendizados.md`, `specs/archive/`, Definition of Done verificável por US, seções Evidências e Comentários do usuário no plano, ADR proposto antes do código, hooks `post-edit-dotnet` e `executor-stop-gate`.
