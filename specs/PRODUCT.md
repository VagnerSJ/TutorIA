# PRODUCT.md — EnglishTutor

## Visão

Um professor de inglês **por voz**, pessoal e local, para um desenvolvedor .NET brasileiro (nível ~B1) treinar fala e escuta todos os dias, sem depender de agenda de professor nem de serviços pagos por uso.

## Persona

**Aluno**: desenvolvedor C#/.NET pleno, brasileiro, inglês ~B1. Lê documentação técnica com facilidade, mas trava ao falar, especialmente em entrevistas. Tem 15–30 minutos por dia.

## Objetivos de aprendizagem

| ID | Objetivo | Como o produto ajuda |
|---|---|---|
| OA1 | Conversação do dia a dia | Modo `free-conversation`: respostas curtas que sempre terminam em pergunta, mantendo o aluno falando |
| OA2 | Vocabulário de desenvolvimento de software | Assuntos de trabalho; sugestão de 1 expressão mais natural por turno |
| OA3 | Entrevistas técnicas em inglês | Modo `job-interview`: entrevistador para vaga C#/.NET pleno/sênior (STAR + perguntas técnicas), uma pergunta por vez |
| OA4 | Compreensão auditiva | Modo `listening-practice` e velocidade de fala configurável |
| OA5 | Precisão gramatical sem quebrar o ritmo | Feedback separado da fala: no máximo 2 correções por turno, só na tela |
| OA6 | Pronúncia | Palavras com baixa confiança do Whisper viram dicas de pronúncia |
| OA7 | Evolução ao longo do tempo | Erros recorrentes salvos e revisados no início de cada sessão |

## Princípios do produto

1. **Fluxo antes de correção**: o professor responde como um interlocutor; correções nunca são faladas.
2. **Local e privado**: áudio, transcrições e histórico ficam na máquina. Só o texto do turno vai ao cérebro (Claude Code) quando esse provedor estiver ativo.
3. **Sem custo por uso**: assinatura do Claude ou modelo local.
4. **Respostas curtas**: 2–3 frases, nível B1–B2.

## Métricas de sucesso (pessoais)

- Latência fim a fim (fim da fala → início do áudio) < 4 s com Claude Code e < 3 s com modelo local.
- ≥ 90% das respostas no formato válido sem retentativa.
- Golden set: ≥ 80% das correções esperadas encontradas pelo provedor escolhido.
- Uso: 5 sessões por semana.

## Fora de escopo (por enquanto)

- Multiusuário, nuvem, aplicativo móvel.
- Avaliação fonética fina (fica para a Fase 8, com wav2vec2).
- Ensino estruturado por currículo (lições, exercícios escritos).

## Também é portfólio

Código limpo, testado (TDD/BDD), arquitetura documentada, ADRs, e o próprio processo SDD com agentes versionado no repositório.
