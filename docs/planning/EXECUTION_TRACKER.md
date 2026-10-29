# Acompanhamento dos passos 1 a 3

Atualizado em 28/10/2026. **Ponto de retomada: passos 1 e 2 concluídos; passo 3 não iniciado.** Não confundir os três passos solicitados com os marcos P0-P8 do plano geral.

| Passo solicitado | Escopo | Situação | Evidência |
|---|---|---|---|
| 1 — Paridade e escopo | Inventário fase 3, donos novos, lacunas e critérios de teste | **Concluído em 28/10/2026** | [Matriz](STEP_1_PARITY.md), [manifesto](PARITY_MANIFEST.json), [testes](evidence/step-1/RESULTS.md) |
| 2 — Modelo operacional | Implementar dados/identidades/estados necessários ao Operation, com testes de domínio | **Concluído em 28/10/2026** | [Modelo](STEP_2_MODEL.md), [testes](evidence/step-2/RESULTS.md), [aceite](STEP_2_ACCEPTANCE.json) |
| 3 — Casos de uso | Implementar fluxo local completo com portas e testes de aplicação | **Não iniciado** | Aguardar retorno ao usuário após passo 2 |

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

## Passo 2 — registro de conclusão

### Implementado

- Dados iniciais completos (OS/veículo/código opcional/serviços/peças), identidade estável de diagnóstico e consumíveis decimais.
- Snapshot imutável e autoritativo de itens/valores, com política identificada e overflow verificado; sem mudar implicitamente cálculo financeiro de consumíveis.
- Mapeamento explícito BudgetJobId/DiagnosticLineId/CatalogJobId, com linhas repetidas de catálogo executadas independentemente.
- Estados locais: Received, InDiagnostic, AwaitingExecutionAuthorization, ReadyForExecution, InExecution, ExecutionCompleted, DiagnosticFailed e Canceled.
- Autorização separada do reparo físico; cancelamento/desativação, tombstone, histórico/versão e datas UTC sem regressão.
- Ajuste mínimo das assinaturas/fila do núcleo Application para compilar com o modelo; regressão preservada com os novos dados. Não são casos de uso completos do passo 3.
- [ADR-006](../architecture/adr-006-modelo-local-e-precificacao.md) registra escolha local, sem atribuir aprovação coletiva dos contratos.

### Validado

- **72 testes aprovados em Release**, 0 falhas/ignorados. Testes anteriores foram reorganizados/evoluídos; não são 19 antigos mais 72 novos.
- **342/342 linhas (100%) e 150/153 branches (98,03%)** instrumentados; gate >=80% de linhas por assembly aprovado.
- Formatação whitespace C# em modo folder verificada e diff conferido.
- Resultado, teste a teste, cobertura e limites em [evidence/step-2](evidence/step-2/RESULTS.md).
- [Critérios AT atualizados](STEP_ACCEPTANCE.md): separar o que o domínio já prova do que depende de aplicação/persistência/integração.

### Limites e parada

Não foram implementados consultas/paginação, fonte de preço por portas, journal, unidade de trabalho isolada, banco, API, broker, Saga ou BDD. O esqueleto Application ainda retorna conflito no recebimento repetido e não cria tombstone ao cancelar operação ausente. O modelo não autentica fonte de preço nem autoriza pagamento/reserva: essas fronteiras dependem de implementação/revisão posteriores. Nenhum arquivo de runtime dos outros repositórios foi alterado.

**Passo 2 concluído. Parar e informar ao usuário antes de iniciar o passo 3.**

## Próximo incremento — somente após retorno ao usuário

**Passo 3:** completar casos de uso locais sobre o novo domínio, com portas de consulta de preços/política explícita, DTOs/queries, métricas de duração, autorizações/cancelamentos e tratamento de intenções repetidas. Criar unidade de trabalho de teste isolada para provar falha de commit sem deixar mutação persistida; não confundir com transação real de banco. Registrar testes/evidência novos e avisar a conclusão.

Contratos reais OS/Budget, cálculo financeiro e dono do catálogo continuam em revisão coletiva. Banco/API/mensageria serão incrementos próprios depois do núcleo funcional.
