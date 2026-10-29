# Acompanhamento dos passos 1 a 3

Atualizado em 28/10/2026. **Ponto de retomada: passo 1 concluído; passo 2 não iniciado.** Não confundir os três passos solicitados com os marcos P0-P8 do plano geral.

| Passo solicitado | Escopo | Situação | Evidência |
|---|---|---|---|
| 1 — Paridade e escopo | Inventário fase 3, donos novos, lacunas e critérios de teste | **Concluído em 28/10/2026** | [Matriz](STEP_1_PARITY.md), [manifesto](PARITY_MANIFEST.json), [testes](evidence/step-1/RESULTS.md) |
| 2 — Modelo operacional | Implementar dados/identidades/estados necessários ao Operation, com testes de domínio | **Não iniciado** | Aguardar checkpoint com usuário após passo 1 |
| 3 — Casos de uso | Implementar fluxo local completo com portas e testes de aplicação | **Não iniciado** | Aguardar conclusão do passo 2 e seu checkpoint |

## Regra de trabalho

- Executar um passo por vez, registrar alterações/testes/limites e avisar conclusão.
- Ao concluir passo 1, parar antes de implementar passo 2, conforme solicitado pelo usuário.
- Próximos passos recebem evidência própria; não reutilizar cobertura antiga como prova de funcionalidade nova.
- Não publicar pacote, implantar ou alterar runtime de outros repositórios nesta sequência inicial.

## Passo 1 — registro de conclusão

### Entregue

- 29 pontos do fluxo com fonte, regra observada, dono na divisão, lacuna atual e critérios de aceite.
- Todos os 23 handlers de casos de uso Workshop/Catalog inventariados; 60 referências de fonte com SHA-256.
- Escopo Operation definido: diagnóstico, fila, progresso dos reparos, histórico e conclusão local. OS, Budget e Inventory mantêm seus dados/efeitos.
- Necessidades de dados do passo 2 registradas: veículo, serviços/peças solicitados, consumíveis, identidade de linha aprovada, atividade e histórico.
- CHG-01..15 distinguem paridade, correção e proposta: peças ausentes do contrato anterior, cálculo de consumíveis, alteração de veículo, desativação, identidade BudgetJobId/LineId, fila e início físico.
- 22 critérios AT documentados para testes de implementação; não marcados como aprovados antes de existir código.
- README, índice, plano geral e avisos de contratos/API apontam para esta baseline de paridade.

### Validado

- `dotnet test` em Release: **19 aprovados, 0 falhas, 0 ignorados**.
- Cobertura Domain/Application atuais: **100% de linhas e branches**, 101/101 linhas e 44/44 branches. Gate >=80% existente aprovado.
- Manifesto conferido contra arquivos de origem; referências a testes e critérios válidas; todos os handlers do recorte têm destino.
- Código de src/tests, scripts e CI permaneceu idêntico ao início do passo, conferido por hash. Passo 2 não começou.
- Nenhum teste do monólito/banco/broker/API/Saga foi executado; cenários legados foram inspecionados como referência.

## Próximo incremento — somente após retorno ao usuário

**Passo 2:** completar o modelo com dados e identidades sem depender de mensagens de outros serviços estarem publicadas. Preservar serviços/peças iniciais, consumíveis decimais e progresso por linha aprovada; testar coleções defensivas, transições, atividade/cancelamento e histórico. Usar [critérios de aceite](STEP_ACCEPTANCE.md), atualizar evidência e avisar ao concluir antes do passo 3.

O contrato wire com OS/Budget, a política financeira de consumíveis e o dono do catálogo ainda precisam de revisão coletiva antes da integração. Isso não impede modelar os dados locais, mas não autoriza declarar compatibilidade distribuída.
