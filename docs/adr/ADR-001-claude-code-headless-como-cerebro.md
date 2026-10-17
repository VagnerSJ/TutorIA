# ADR-001 — Claude Code headless como cérebro principal, atrás de uma porta plugável

- **Status:** Aceito
- **Data:** 2026-10-17
- **US relacionadas:** US-004, US-005, US-007

## Contexto

O professor precisa de um modelo de linguagem bom em correção gramatical e conversa natural. O aluno já tem assinatura do Claude e não quer pagar por uso de API. A GPU local (RTX 4070 Ti SUPER, 16 GB) consegue rodar modelos de 7–14B quantizados, que provavelmente são piores em correções sutis. O projeto também é portfólio, então a troca de provedores deve ser demonstrável.

## Decisão

Usar o **Claude Code em modo não interativo** (`claude -p`) como provedor principal, implementado diretamente como `ITutorBrain` (processo por turno na Fase 2, processo persistente com `stream-json` na Fase 6), atrás de uma porta que também aceita o Ollama local e, no futuro, a API da Anthropic. O histórico da conversa pertence à aplicação (RN-BRAIN01).

Invocação isolada: pasta de trabalho vazia e dedicada, `--system-prompt-file` (substitui o prompt padrão), `--tools ""`, `--disallowedTools "mcp__*"`, `--strict-mcp-config`, `--permission-mode dontAsk`. **Sem `--bare`**, porque ele exige `ANTHROPIC_API_KEY` e não usa a assinatura.

## Alternativas consideradas

| Alternativa | Prós | Contras |
|---|---|---|
| API da Anthropic (`IChatClient`) | Latência menor, API estável, streaming nativo | Custo por uso; exige API key |
| Só Ollama local | Grátis, offline, latência previsível | Qualidade de correção inferior em modelos ≤14B |
| Claude Code headless (escolhida) | Usa a assinatura existente; modelos de ponta | Inicialização de processo por turno (latência); limites de uso; acoplamento a uma CLI |

## Consequências

- Positivas: qualidade alta sem custo extra; arquitetura de portas fica demonstrada com 2+ provedores reais.
- Negativas: latência de inicialização do processo; o system prompt é gravado na sessão no primeiro turno (mudanças no `tutor.md` só valem em sessões novas); dependência do formato JSON da CLI → fixtures gravadas e testes de contrato.
- A medir: latência por turno (processo por chamada × persistente, US-004 e Fase 6); frequência de limite de uso → fallback para Ollama (US-007).
