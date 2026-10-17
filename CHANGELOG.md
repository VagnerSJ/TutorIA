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
- Fixed: `/quit` enviado por pipe no Windows PowerShell 5.1 era lido com BOM UTF-8 e tratado como texto livre (US-001).
- Hábitos de trabalho com IA: skill `revisar-contexto`, `docs/aprendizados.md`, `specs/archive/`, Definition of Done verificável por US, seções Evidências e Comentários do usuário no plano, ADR proposto antes do código, hooks `post-edit-dotnet` e `executor-stop-gate`.
