# Fixtures de erro do Claude Code (US-005)

Cada caso tem três arquivos: `<caso>.stdout.txt`, `<caso>.stderr.txt` e `<caso>.exit-code.txt`.
São lidos por `Support/ClaudeErrorFixtures.Read(caso)`.

- Gravação: 2026-10-21, `claude --version` = `2.1.304 (Claude Code)`.
- Nenhuma anonimização foi necessária (a saída gravada não tem caminho pessoal, e-mail, chave nem nome do titular).

| Caso | Origem | Tipo esperado |
|---|---|---|
| `not-authenticated` | **gravado**: `CLAUDE_CONFIG_DIR` apontando para uma pasta vazia | `NotAuthenticated` |
| `unknown-model` | **gravado**: `--model nao-existe-us005` com login válido | `Unknown` |
| `usage-limit-synthetic-1` | **sintético**: o stdout gravado de `not-authenticated`, com o `result` trocado por `Claude AI usage limit reached\|1760000000 (synthetic)`; mesmo código de saída | `UsageLimitReached` |
| `usage-limit-synthetic-2` | **sintético**: stdout vazio, stderr `You've hit your limit - resets 5pm (synthetic)`, código 1 | `UsageLimitReached` |

## Por que há fixtures sintéticas

O limite de uso da assinatura não é reproduzível sob demanda (decisão do usuário, P4 do plano da US-005).
**Na primeira ocorrência real de limite de uso, gravar a saída e trocar o sintético pela gravada.**

## Comando de gravação

A partir da raiz do repositório, no Git Bash, com uma pasta de trabalho temporária e vazia, sem `CLAUDECODE`,
`ANTHROPIC_BASE_URL` e `CLAUDE_CODE_*` herdados:

```bash
echo 'Student said (transcribed): "hi"' | claude -p --output-format json \
  --system-prompt-file prompts/tutor.md --append-system-prompt-file prompts/format.tags.md \
  --tools "" --disallowedTools "mcp__*" --strict-mcp-config --permission-mode dontAsk --model <modelo>
```

- `not-authenticated`: modelo `haiku` e `CLAUDE_CONFIG_DIR=<pasta temporária vazia>`.
- `unknown-model`: modelo `nao-existe-us005`.

Os argumentos de isolamento são os do adaptador (RN-BRAIN06; `ClaudeCodeTutorBrain.BuildArguments`).

## Diferenças em relação à chamada do adaptador

- modo Tags (`--append-system-prompt-file prompts/format.tags.md`), sem `--json-schema`: o esquema é uma constante do código (`TutorReplySchema.Json`) e as duas falhas gravadas acontecem antes de qualquer resposta do modelo;
- system prompt = o arquivo-modelo `prompts/tutor.md` sem renderizar (o adaptador grava o prompt renderizado num arquivo temporário);
- sem `--resume` (primeira chamada de uma sessão);
- entrada padrão de uma linha (`Student said (transcribed): "hi"`).
