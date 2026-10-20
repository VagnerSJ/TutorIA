---
name: checklist-revisao
description: Checklist objetivo usado pelo Revisor para avaliar o diff de uma US do EnglishTutor (spec, arquitetura, testes, qualidade, segurança, documentação). Use ao revisar código ou um pull request deste projeto.
user-invocable: false
---

# Checklist de revisão

Marque cada item como ✅, ❌ ou N/A. Itens com **[O]** são obrigatórios: ❌ neles é **Bloqueante**. Os demais viram **Sugestão**, salvo se o problema também violar uma regra `RN-*` ou o `ARCHITECTURE.md`.

## 0. Revisão de plano (modo revisão de plano, antes do código)
- [O] Todo cenário BDD da US aparece no mapa da §4 com projeto, classe e método no padrão `Metodo_Cenario_Resultado`.
- [O] Cada resposta P* com efeito observável (texto exibido, código de saída, normalização, evento de log) tem teste mapeado na §4 e tarefa na §5.
- [O] A DoD da §10 cobre a DoD da US na sprint, com comandos verificáveis e resultado esperado (inclui cobertura do Core ≥ 80%).
- [O] Conformidade com R1–R4, `RN-*` citadas e regras rápidas do `CLAUDE.md`; decisões técnicas relevantes têm ADR Proposto.
- [O] Escopo: nada além nem aquém do que a US pede; fora de escopo explícito. Item da §8 adiado para outra US tem texto exato a registrar na spec da US de destino, que você confere (AP-022). Procure adiamentos também nas P* e nos comentários de teste ("a US-YYY troca/atualiza"), e confira que todo "limite aceito" lista os consumidores do componente e o efeito em cada um (AP-040).
- [O] Consistência interna: §3 (arquivos) × §4 (testes) × §5 (tarefas) × §6 (pacotes), com contagens que batem e nomes de teste da §5 idênticos aos da §4 (AP-025).
- Transformações citadas nas tabelas de mensagens da §2 (corte, normalização, limite) têm teste nos dois lados do limite (AP-016); asserção sobre mensagem que embute texto de biblioteca confere um trecho que só o projeto produz (AP-015).
- [O] Ordem das tarefas preserva o Red de cada teste (nenhuma tarefa anterior já faz o teste seguinte nascer verde).
- Critério `LiveBrain` sobre saída de modelo diz a camada medida (adaptador ou serviço com retentativa), a tolerância e quantas rodadas seguidas precisa sobreviver; com tolerância zero, o plano registra o risco de oscilação e quem decide quando ficar vermelho (AP-032).
- US com `ValidateOnStart` em mais de uma classe de Options tem teste com duas seções inválidas ao mesmo tempo; valor aceito sem diferenciar maiúsculas pelo validador tem teste no consumidor com caixa diferente (AP-039).
- Justificativas do plano que citam cenário ou regra de outra US estão certas contra o `SPRINT_N.md` (papéis, `Dado`, provedor principal ou reserva), não só os textos a registrar na spec de destino (AP-036).
- Verificação de "nenhuma linha" na DoD (busca de texto antigo) tem o outro lado demonstrado: o mesmo comando acha as ocorrências antes da mudança; a busca cobre o nome antigo do cenário e as variações sintáticas da asserção (AP-035).
- Alteração proposta na `ARCHITECTURE.md` lista também as linhas do `CLAUDE.md` e das skills que repetem a mesma regra.
- Riscos técnicos e ambiguidades que travariam o Executor estão tratados ou viraram pergunta em aberto.
- Viabilidade técnica no SDK e nas bibliotecas em uso.

## Como escrever bloqueantes e sugestões
- Descreva o **efeito esperado** e onde está o problema, não a técnica de implementação ou de teste. Ex.: "a mensagem de configuração inválida não pode se perder se o cancelamento chegar durante a escrita", e não "teste com token já cancelado". Cabe ao Executor escolher a técnica e confirmar que o Red falha pelo motivo certo (AP-010).
- Identifique o trecho pelo símbolo (classe/método) além do número de linha, que envelhece (AP-023).
- Afirmação quantitativa sobre comportamento existente (limite, contagem, "a partir de qual turno") cita o símbolo que a sustenta (ex.: `ConversationWindow.Add`) e foi conferida no código antes de ir para o relatório (AP-036).

## 1. Spec e regras
- [O] Todo cenário BDD da US tem pelo menos um teste correspondente (tabela da seção 4 do plano confere com o código).
- [O] Comportamento implementado bate com os cenários (valores, mensagens, códigos de saída).
- [O] Regras `RN-*` citadas pela US são respeitadas; testes citam a regra no comentário.
- [O] Nada fora do escopo da US foi implementado sem registro no plano.
- [O] Mudanças de comportamento estão primeiro na spec/plano e decisões técnicas relevantes têm ADR commitado **antes** do código que as implementa.
- [O] Todos os comandos da Definition of Done foram rodados pelo Executor, com resultado colado em "Evidências" no `plan.md`, e o resultado confere com o seu.
- Comentários do usuário no `plan.md` foram respondidos e respeitados.
- [O] Escolha de forma que muda texto exibido, mensagem de exceção ou nome público está no §12 em commit anterior ao `feat`/`fix` que a implementa (AP-014, AP-027). Nos demais casos (nomes privados, detalhes internos), o mesmo item é sugestão.

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
- [O] Testes unitários sem rede, sem GPU e sem processo real fora de `Category=LiveBrain`; a suíte unitária roda em segundos (alvo < 30 s).
- Testes determinísticos: sem `Thread.Sleep` nem dependência de relógio real.
- Mensagens de erro (parser, validação de Options, exceções de domínio) dizem o que está errado e o que se espera.
- Nomes `Metodo_Cenario_Resultado`; um comportamento por teste.

## 4. Qualidade do código
- [O] `CancellationToken` repassado em toda cadeia assíncrona; sem `.Result`/`.Wait()`.
- [O] Sem `catch` que engole exceção; `OperationCanceledException` propaga.
- [O] Processos externos com `ArgumentList`, timeout e encerramento da árvore do processo.
- Options validadas na inicialização; nenhum `IConfiguration` fora do composition root.
- Logs estruturados (templates), sem interpolação e sem dados sensíveis.
- Sem código morto, TODO sem US, números mágicos sem constante/configuração.
- Tipos `sealed`/`record` conforme `padroes-codigo`.
- Nenhum caractere invisível literal no diff (BOM, zero-width, espaço sem quebra); use escape `\uXXXX`. Confira por bytes (BOM, zero-width space, espaço sem quebra) com `git diff develop...HEAD | LC_ALL=C grep -c -e $'\xef\xbb\xbf' -e $'\xe2\x80\x8b' -e $'\xc2\xa0'` (Git Bash), que deve dar 0 (o código de saída 1 com contagem 0 é o esperado). Esta é a forma única do projeto: o Planejador a copia para a DoD e o Executor a usa, sem variantes (AP-019).

## 5. Segurança e privacidade
- [O] Nenhum segredo, token, caminho pessoal ou dado do usuário commitado (inclui fixtures).
- [O] `.gitignore` cobre modelos, bancos, áudios e configurações locais novas.
- [O] Provedor Claude Code: sem `--bare`, sem ferramentas, pasta de trabalho isolada (RN-BRAIN05/06).

## 6. Git e documentação
- [O] Commits convencionais com escopo; branch `feature/US-XXX_slug`.
- Plano com tarefas marcadas `[x]`.
- Novas chaves de configuração presentes no `appsettings.json` com valor padrão comentado no plano/doc.
