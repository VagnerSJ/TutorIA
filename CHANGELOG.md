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
- Added: `OllamaTutorBrain`, professor local sobre `IChatClient` (`OllamaSharp`): histórico completo e `format` com o JSON Schema do `TutorReply` em `/api/chat`; com `Brain:Provider = Ollama` o console conversa pelo `ConversationService` (US-006).
- Added: falhas do Ollama com mensagem que diz o que houve e o que fazer: "Ollama is not running", "ollama pull <modelo>", tempo esgotado e falha genérica com detalhe (US-006).
- Added: configuração `Brain:Ollama:*` (`Endpoint`, `Model`, `Temperature`, `TimeoutSeconds` padrão 60 com faixa 1 a 3600, `OutputMode`) validada na inicialização (RN-BRAIN09) (US-006).
- Added: ADR-009 (provedor Ollama: `IChatClient`, falhas e tempo máximo) e fixtures do Ollama gravadas pelo próprio adaptador (US-006).
- Changed: RN-FEED01: o validador aceita 1 ou 2 perguntas no `reply` (tolerância de verificação; o prompt continua pedindo exatamente uma); 0 e 3 ou mais continuam recusadas, e a mensagem de recusa diz quantas encontrou e que 1 ou 2 são aceitas (US-006).
- Changed: prompts `format.json.md` e `format.tags.md` reforçados com a contagem de `?` e um exemplo proibido, para o modelo local de 14B (US-006).
- Changed: o D5 do Ollama mede o adaptador dentro do `ConversationService`, contando a retentativa única de formato (ADR-006) (US-006).
- Removed: `TutorNotConnectedInputHandler` e o texto "The tutor is not connected yet." (US-006).
- Hábitos de trabalho com IA: skill `revisar-contexto`, `docs/aprendizados.md`, `specs/archive/`, Definition of Done verificável por US, seções Evidências e Comentários do usuário no plano, ADR proposto antes do código, hooks `post-edit-dotnet` e `executor-stop-gate`.
- Added: `BrainFactory` escolhe o cérebro por `Brain:Provider`; o seletor provisório da US-006 foi removido, e cada cérebro também fica registrado com chave (nome do provedor) (US-007).
- Added: cérebro reserva (`Brain:FallbackProvider`, vazio por padrão): `UsageLimitReached`, `Timeout`, `CliNotFound` e `NotAuthenticated` do primário mandam o turno ao reserva e o console mostra `Switched to <reserva> (<motivo>)` antes da resposta; sem reserva a falha aparece como antes e a sessão continua (RN-BRAIN08) (US-007).
- Added: `FallbackTutorBrain` (decorator), `BrainFallbackPolicy` e a porta `IBrainSwitchNotifier` no Core, implementada pelo console; ADR-010 (US-007).
- Added: `Brain:FallbackProvider` validado na partida: provedor desconhecido ou igual a `Brain:Provider` encerra o app com código 2 e mensagem que diz o esperado (US-007).
- Changed: sem a partida do host (ex.: DI nos testes), `Brain:Provider` vazio ou inválido deixa de cair em silêncio no Claude Code e lança erro com os provedores aceitos (US-007).
- Added: suíte de contrato do `ITutorBrain` (`TutorBrainContractTests`) executada para `ClaudeCode` e `Ollama` com fakes de processo e de cliente de chat: formatos de saída, perguntas, janela completa, ausência de estado, timeout, inalcançável, cancelamento e retentativa (RN-BRAIN01, RN-BRAIN03, RN-BRAIN04, RN-BRAIN07, RN-BRAIN09) (US-008).
- Added: guardas por reflexão que exigem uma subclasse por provedor e mantêm o trait `LiveBrain` só nas classes `*LiveTests` (US-008).
- Added: testes de conformidade `LiveBrain` (`ClaudeCodeContractLiveTests`, `OllamaContractLiveTests`) contra os provedores reais, excluídos de `Category!=LiveBrain` (US-008).
- Added: comando `EnglishTutor.Console eval [--providers] [--golden-set] [--reports-dir]` que roda um golden set de 32 frases (`eval/golden-set.json`) por provedor e grava um relatório markdown em `eval/reports/` com correções esperadas encontradas, formato válido sem e com retentativa, latência média e p95 (US-009).
- Added: nota mínima `met` / `not met` no relatório (correções >= 80% e formato sem retentativa >= 90%), usada na DoD das US de cérebro seguintes; provedor fora do ar sai como `unavailable` com o motivo e o código de saída continua 0 (US-009).
- Added: `Core/Evaluation` (`GoldenSetParser` com erros que dizem o que está errado e o que se espera, `CorrectionMatcher`, `EvalRunner`, `EvalMetrics`, `EvalThresholds`, `EvalReportBuilder`), `eval/README.md` e ADR-011 (US-009).
- Added: `eval` com argumentos inválidos ou golden set ausente/inválido termina com código 2, mensagem na saída de erro e sem relatório (US-009).
- Added: portas de voz e áudio no Core (`ISpeechToText`, `ITextToSpeech`, `IAudioRecorder`, `IAudioPlayer`, `Transcript`, `TranscribedWord`) e `AudioClip` (PCM mono float32) que rejeita taxa <= 0 e clipe vazio, com fakes de teste (US-010).
- Added: opções `Tts:Speed` (0.5 a 1.5, padrão 1.0), `Stt:LowConfidenceThreshold` (0 a 1, padrão 0.6) e `Voice:Enabled` (padrão false) no `appsettings.json`, validadas na partida; valor fora da faixa encerra com código 2 e mensagem que diz a faixa (US-010).
- Changed: filtro padrão de testes passa a ser `Category!=LiveBrain&Category!=LiveVoice` em hooks, CI, README, agentes e skills (US-010).
