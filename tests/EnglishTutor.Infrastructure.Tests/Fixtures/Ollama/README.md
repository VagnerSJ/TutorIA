# Fixtures do Ollama (US-006)

Respostas reais de `POST /api/chat`, gravadas pelo próprio adaptador (`OllamaTutorBrain`) com um `DelegatingHandler` de gravação por baixo do `OllamaApiClient` (AP-048). Gerador: `OllamaFixtureRecorderLiveTests` (`Category=LiveBrain`), que escreve em `bin/.../recorded-fixtures/`; os quatro arquivos abaixo foram copiados de lá.

- Data: 2026-10-21
- `ollama --version`: `ollama version is 0.40.7` (instalado com `winget install Ollama.Ollama`)
- Modelo: `qwen2.5:14b-instruct-q4_K_M` (baixado com `ollama pull`; 9,0 GB)
- Comando: `dotnet test --filter "Category=LiveBrain&FullyQualifiedName~OllamaFixtureRecorderLiveTests"`
- Pacote cliente: `OllamaSharp` 5.5.0

| Arquivo | O que é |
|---|---|
| `chat-structured.json` | corpo da resposta para a frase `I have worked with C# since five years`, com `OutputMode = JsonSchema` (`format` com o `TutorReplySchema`). O campo `message.content` é JSON puro (sem cerca markdown nem texto em volta) |
| `chat-request.json` | corpo da requisição que o adaptador enviou nessa chamada (documenta o fio: `model`, `messages` com `system`/`user`, `format`, `options.temperature`; não é lido por teste) |
| `model-not-found.status.txt` | status HTTP da resposta para `Model = nao-existe-us006`: `404` |
| `model-not-found.json` | corpo dessa resposta: `{"error":"model 'nao-existe-us006' not found"}` |

## Diferenças em relação à chamada do adaptador

- A requisição gravada é a do adaptador (mesmo `OllamaChatClients.Create`, mesmo `prompts/tutor.md`, mesmo arquivo de formato e mesma mensagem de aluno montada pelo `TurnMessageBuilder`). O teste usa a pasta `prompts` copiada para a saída do teste, não a do aplicativo instalado.
- A primeira chamada do modelo inclui o carregamento dele na VRAM: `load_duration` de cerca de 30,8 s e `total_duration` de cerca de 54,7 s em `chat-structured.json`. Chamadas com o modelo aquecido são bem mais rápidas.
- Cadeia de exceções observada (não é fixture; `exceptions.txt` do gravador): modelo ausente chega como `System.Net.Http.HttpRequestException` com `StatusCode` `NotFound` (404); conexão recusada chega como `HttpRequestException` sem `StatusCode`, com `SocketException` interna.
