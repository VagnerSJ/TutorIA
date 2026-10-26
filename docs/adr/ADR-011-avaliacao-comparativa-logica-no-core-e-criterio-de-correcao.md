# ADR-011 — Avaliação comparativa: lógica no Core, comando no Console e critério de "correção encontrada"

- **Status:** Aceito (2026-10-23)
- **Data:** 2026-10-23
- **US relacionadas:** US-009 (e, como consumidoras, as US de cérebro das sprints seguintes, que usam a nota mínima do `eval` na DoD)

## Contexto

A US-009 compara os cérebros com um golden set de cerca de 30 frases com erros conhecidos e mede, por provedor: correções esperadas encontradas, respostas no formato, latência média e p95. Forças:

- a ARCHITECTURE R1/R4 manda a regra de negócio para o Core e deixa o Console como composition root; o PRODUCT fixa as metas (≥ 80% das correções esperadas; ≥ 90% no formato **sem retentativa**) e a spec da US repete esses limiares como nota mínima das US seguintes;
- o ADR-010 já registra cada cérebro concreto com chave (`ITutorBrain` com o nome do provedor), justamente para o `eval` usar cada um sem reserva;
- a retentativa de formato vive no `ConversationService` (ADR-006): medir "sem retentativa" exige olhar a primeira chamada ao cérebro, e medir o que o aluno realmente recebe exige passar pelo serviço;
- a medição precisa ser testável sem rede, sem GPU e sem modelo (fakes), e repetível: o resultado de "encontrou a correção" não pode depender de outro modelo julgando;
- o Ollama indisponível é `Unknown` (ADR-009), e o Claude Code sem CLI é `CliNotFound`: o `eval` precisa decidir sozinho o que é "provedor indisponível" sem esperar 30 vezes o tempo máximo.

## Decisão

1. **Lugar.** A lógica da avaliação mora em `EnglishTutor.Core/Evaluation` (BCL apenas, `System.Text.Json` incluído): modelo e leitura do golden set, comparação de correções, execução por provedor, métricas, nota mínima e montagem do relatório markdown. O Console só interpreta os argumentos (`Commands/`), lê o arquivo, resolve os cérebros por chave, grava o relatório e escreve na tela. A Infrastructure não muda.
2. **Execução.** Para cada provedor e para cada frase, um `ConversationService` novo (janela vazia, modo `free-conversation`) sobre o cérebro do provedor **sem reserva**, envolvido por um contador de chamadas. Isso mede o que o aluno receberia e, ao mesmo tempo, qual das tentativas deu certo. Frases e provedores rodam em sequência.
3. **Correção esperada encontrada.** Cada frase do golden set traz 1 ou 2 correções esperadas, cada uma com `original` (trecho da frase) e `accepted` (uma ou mais formas corrigidas aceitas). Está encontrada quando o `Corrected` de alguma correção da resposta contém, como sequência contígua de palavras, uma das formas aceitas. Palavras: minúsculas, sem pontuação nas bordas, aspas e apóstrofos tipográficos normalizados. Só `Corrected` conta (não `Original` nem `Explanation`). Mede cobertura: correções a mais não são penalizadas. O carregamento recusa uma frase cujo `original` não esteja no texto ou cuja forma aceita já esteja no texto (uma correção que o aluno não fez não pode contar como achada).
4. **Formato.** Duas taxas por provedor: "válido sem retentativa" (a primeira chamada ao cérebro devolveu um `TutorReply`; é a métrica do PRODUCT e a que vale para a nota mínima) e "válido com a retentativa única" (a resposta final não é `IsRawFallback`; só informativa). "Válido" é o que o parser e o `TutorReplyValidator` aceitam, inclusive a tolerância de 1 ou 2 perguntas da RN-FEED01.
5. **Provedor indisponível.** Se a primeira frase de um provedor termina em `BrainException`, o provedor fica `unavailable` com a mensagem da exceção como motivo, e as outras frases não rodam. Uma `BrainException` em frase posterior conta como erro daquela frase (formato inválido, nenhuma correção, fora da latência) e a avaliação continua. Cancelamento propaga.
6. **Nota mínima.** `EvalThresholds`: correções encontradas ≥ 80% e formato sem retentativa ≥ 90%, comparados com inteiros (`encontradas × 100 ≥ 80 × esperadas`). O relatório diz `met` ou `not met` na linha de cada provedor avaliado; o código de saída do comando não depende da nota.
7. **Latência.** Tempo do turno inteiro por frase, retentativa incluída, medido com `TimeProvider`; média em ticks (divisão inteira) e p95 pelo método do posto mais próximo (`posto = ceil(0,95 × n)`, calculado com inteiros). Frases com erro ficam fora da latência.
8. **Golden set.** `eval/golden-set.json`, campos `reviewedBy` (vazio até o usuário revisar) e `sentences[{id, text, expected[{original, accepted[]}]}]`; no máximo 2 correções esperadas por frase (RN-FEED02: o modelo devolve no máximo 2 por turno).

## Alternativas consideradas

| Alternativa | Prós | Contras |
|---|---|---|
| Outro modelo como juiz ("a correção cobre o erro?") | Aceita paráfrases | Não determinístico, caro (assinatura/GPU), difícil de testar com fakes, e o juiz teria de ser um dos cérebros avaliados |
| Casar pelo `original` da resposta (o modelo apontou o mesmo trecho) | Independe de como o modelo reescreve | Acerta o trecho e erra a correção; trechos longos ou curtos demais casam por acaso |
| Igualdade exata do `Corrected` | Simples | Quase nunca iguala: o modelo devolve a frase inteira ou um trecho maior |
| Lógica toda no Console | Menos tipos | Contraria R4; fora da meta de cobertura do Core; difícil de testar sem subir o host |
| Novo projeto `EnglishTutor.Eval` | Isola a avaliação | Mais um projeto para a solução, sem ganho: o Core já tem as portas e o Console já é o composition root |
| Medir só o formato da chamada direta ao cérebro | Mais simples | Não diria o que o aluno recebe com a retentativa (ADR-006), que decidiu o D5 da US-006 |
| `BrainFactory.Create(provider)` público | Reaproveita a validação da fábrica | As chaves do ADR-010 já entregam cada cérebro sem reserva; a fábrica não precisa mudar |

## Consequências

- Positivas: a medição roda nos testes unitários com fakes e relógio falso; os mesmos números do PRODUCT passam a ter dono e comando; a retentativa é medida pela primeira vez (pendência registrada na US-008, limite L1); a Infrastructure fica intocada.
- Negativas / riscos:
  - **Só cobertura.** Uma correção errada ou desnecessária não derruba a nota. Consumidores: a nota mínima (D6) e as US de cérebro futuras que a usam na DoD; efeito: um modelo que "corrige demais" passa. Mitigação parcial: no máximo 2 correções por resposta (RN-FEED02).
  - **Paráfrase conta como erro.** Se o modelo corrige certo com palavras que o usuário não listou em `accepted`, a taxa cai. Consumidores: nota mínima e relatório (a tabela de detalhes mostra cada frase para o usuário ampliar `accepted`).
  - **Indisponibilidade decidida pela primeira frase.** Um `Timeout` na primeira frase (por exemplo, o Ollama carregando o modelo) marca o provedor como `unavailable`. Consumidores: relatório (mostra o motivo), D5/D6 da US (o Executor aquece o modelo antes).
  - **Consome a assinatura.** Uma rodada do Claude Code gasta cerca de 30 a 60 chamadas.
  - A nota mínima depende do golden set: mudar o arquivo muda a régua.
- O que precisa ser medido ou revisitado: a taxa de retentativa por provedor (primeira medição desta US); se 2 correções por resposta bastam para frases com 2 erros esperados; a margem do Claude Code em 80% (a fumaça da US-006 achou 4/5); se a nota mínima do Ollama deve existir (hoje só o ClaudeCode tem meta).
