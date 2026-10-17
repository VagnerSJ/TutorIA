---
name: checklist-revisao
description: Checklist objetivo usado pelo Revisor para avaliar o diff de uma US do EnglishTutor (spec, arquitetura, testes, qualidade, segurança, documentação). Use ao revisar código ou um pull request deste projeto.
user-invocable: false
---

# Checklist de revisão

Marque cada item como ✅, ❌ ou N/A. Itens com **[O]** são obrigatórios: ❌ neles é **Bloqueante**. Os demais viram **Sugestão**, salvo se o problema também violar uma regra `RN-*` ou o `ARCHITECTURE.md`.

## 1. Spec e regras
- [O] Todo cenário BDD da US tem pelo menos um teste correspondente (tabela da seção 4 do plano confere com o código).
- [O] Comportamento implementado bate com os cenários (valores, mensagens, códigos de saída).
- [O] Regras `RN-*` citadas pela US são respeitadas; testes citam a regra no comentário.
- [O] Nada fora do escopo da US foi implementado sem registro no plano.

## 2. Arquitetura (`ARCHITECTURE.md`)
- [O] `EnglishTutor.Core` sem referência a Infrastructure/Console/pacotes de terceiros (R1).
- [O] Regra de negócio no Core, não em adaptadores (R4).
- [O] Novos adaptadores implementam uma porta e são registrados por configuração (R3).
- [O] Mudança arquitetural tem ADR e `ARCHITECTURE.md` atualizado.
- Nomes de tipos/pastas seguem a estrutura da §3.

## 3. Testes
- [O] `dotnet build` sem warnings.
- [O] `dotnet test --filter "Category!=LiveBrain"` verde.
- [O] Cobertura de linhas do `EnglishTutor.Core` ≥ 80%.
- [O] Nenhum teste apagado, ignorado (`Skip`) ou enfraquecido sem justificativa no plano.
- Ordem de commits mostra teste antes do código (Red → Green). Desvio pontual = sugestão; ausência geral = bloqueante.
- Testes determinísticos: sem `Thread.Sleep`, sem dependência de relógio real, rede ou processo real fora de `LiveBrain`.
- Nomes `Metodo_Cenario_Resultado`; um comportamento por teste.

## 4. Qualidade do código
- [O] `CancellationToken` repassado em toda cadeia assíncrona; sem `.Result`/`.Wait()`.
- [O] Sem `catch` que engole exceção; `OperationCanceledException` propaga.
- [O] Processos externos com `ArgumentList`, timeout e encerramento da árvore do processo.
- Options validadas na inicialização; nenhum `IConfiguration` fora do composition root.
- Logs estruturados (templates), sem interpolação e sem dados sensíveis.
- Sem código morto, TODO sem US, números mágicos sem constante/configuração.
- Tipos `sealed`/`record` conforme `padroes-codigo`.

## 5. Segurança e privacidade
- [O] Nenhum segredo, token, caminho pessoal ou dado do usuário commitado (inclui fixtures).
- [O] `.gitignore` cobre modelos, bancos, áudios e configurações locais novas.
- [O] Provedor Claude Code: sem `--bare`, sem ferramentas, pasta de trabalho isolada (RN-BRAIN05/06).

## 6. Git e documentação
- [O] Commits convencionais com escopo; branch `feature/US-XXX_slug`.
- Plano com tarefas marcadas `[x]`.
- Novas chaves de configuração presentes no `appsettings.json` com valor padrão comentado no plano/doc.
