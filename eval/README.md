# Avaliação comparativa dos cérebros (`eval`)

O comando `eval` roda um conjunto de frases com erros conhecidos (o *golden set*) por cada provedor de cérebro e grava um relatório em markdown com a nota mínima (`met` / `not met`). Decisão: [ADR-011](../docs/adr/ADR-011-avaliacao-comparativa-logica-no-core-e-criterio-de-correcao.md). Visão geral: `ARCHITECTURE.md` §6.6.

## Como rodar

Windows PowerShell 5.1 (na raiz do repositório):

```powershell
dotnet run --project src/EnglishTutor.Console -- eval --providers ClaudeCode,Ollama
```

Bash:

```bash
dotnet run --project src/EnglishTutor.Console -- eval --providers ClaudeCode,Ollama
```

Opções (todas opcionais):

| Opção | Padrão | Significado |
|---|---|---|
| `--providers <lista>` | todos (`ClaudeCode`, `Ollama`) | Provedores a avaliar, separados por vírgula (ou por espaço) |
| `--golden-set <arquivo>` | `eval/golden-set.json` | Arquivo com as frases |
| `--reports-dir <pasta>` | `eval/reports` | Pasta do relatório (criada se não existir; ignorada pelo Git) |

O relatório se chama `eval-<aaaaMMdd-HHmmss>.md` (UTC). O código de saída é 0 quando o relatório é gravado (qualquer nota), 2 para argumentos ou golden set inválidos e 1 se o relatório não puder ser gravado. Um provedor cuja **primeira** frase falha (CLI ausente, Ollama fora do ar etc.) aparece como `unavailable` com o motivo.

Antes de avaliar o Ollama, aqueça o modelo (uma chamada qualquer): a primeira frase com o modelo ainda carregando pode estourar o tempo limite e marcar o provedor como `unavailable`.

## Formato de `golden-set.json`

```json
{
  "reviewedBy": "",
  "sentences": [
    { "id": "gs-001", "text": "I have worked with C# since five years",
      "expected": [ { "original": "since five years", "accepted": ["for five years"] } ] }
  ]
}
```

- `reviewedBy`: nome de quem revisou as frases. **Fica em branco até o usuário revisar o arquivo**; enquanto estiver vazio, o relatório diz `pending (reviewedBy is empty)`. Quem preenche é o usuário, não um agente.
- `sentences`: ao menos uma frase (a suíte exige 30 ou mais no arquivo do repositório). Cada frase tem `id` único (sem diferenciar maiúsculas), `text` e de 1 a 2 itens em `expected` (RN-FEED02).
- `expected[].original`: trecho errado, que precisa estar no `text`.
- `expected[].accepted`: formas corrigidas aceitas (ao menos uma); nenhuma pode já estar no `text`.
- Nomes de propriedade em camelCase e sensíveis a maiúsculas; propriedades desconhecidas são ignoradas.

Um arquivo inválido faz o comando terminar com código 2 e listar **todos** os problemas, cada um dizendo o que está errado e o que se espera.

## Como o critério casa

Uma correção esperada conta como encontrada quando **algum** `Corrected` da resposta do tutor contém, como palavras lado a lado e na mesma ordem, uma das formas de `accepted`. Antes de comparar, o texto vira minúsculas, as aspas e apóstrofos tipográficos viram retos e a pontuação das bordas de cada palavra é descartada (`c#`, `don't` e `e-mail` ficam como estão). O `original` e a `explanation` da resposta **não** entram, e correções a mais não são penalizadas: o critério mede só cobertura.

## Nota mínima

`met` quando as correções esperadas encontradas são ≥ 80% **e** as respostas no formato válido **sem retentativa** são ≥ 90%; senão `not met`. A taxa com a retentativa única é informativa. Latência média e p95 não têm limiar.

## Como ampliar `accepted`

Se o tutor corrige certo com palavras que não estão em `accepted`, a correção conta como não encontrada. Abra a seção `Details` do relatório, veja a frase e acrescente a forma correta em `accepted` (mudar o arquivo muda a régua; registre a revisão em `reviewedBy`). Nunca inclua em `accepted` um trecho que já está no `text`.
