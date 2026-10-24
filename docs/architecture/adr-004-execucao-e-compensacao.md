# ADR-004: Autorização e início físico do reparo

**Status:** Proposed. **Date:** 2026-10-24. **Decided by:** baseline local solicitada; revisão coletiva pendente. **Implementation:** parcial ou pendente conforme README.

## Context

O núcleo inicial entra em InExecution quando aceita orçamento/reserva. Isso confunde entrada na fila com início físico. Cancelamento concorrente pode liberar reserva enquanto o reparo começa.

## Decision

Propor StartExecution como autorização: produz ReadyForExecution e ExecutionAuthorized. Ação staff separada inicia fisicamente, produz InExecution e ExecutionStarted. CancelOperation antes do início grava fence terminal/tombstone idempotente. Saga aguarda OperationCanceled antes de liberar estoque ou reembolsar. Se início vencer a concorrência, rejeitar cancelamento e recuperar manualmente/por retry. ExecutionFinished conclui apenas trabalho local; OS aguarda StockConsumed quando há estoque.

**Why:** Permitir cancelamento seguro da fila sem fingir que trabalho físico pode ser desfeito.

## Consequences

### Positive

Ponto sem volta explícito e testável.

### Negative

Acrescenta estado, evento e protocolo de confirmação; exige migração do núcleo atual e revisão dos diagramas Budget.

### Mitigations

Concorrência no mesmo agregado para cancelamento/início; testes com mensagens atrasadas, tombstone e compensação indisponível.

## Alternatives Considered

Tratar StartExecution como início físico: simples, mas cancela a flexibilidade da fila e não descreve trabalho humano. Cancelar sem confirmação: risco de estorno/liberação com reparo em curso. Desfazer reparo: não é compensação técnica possível.

## Related

- [Visão geral](OVERVIEW.md).
- [Saga](SAGA.md).
- [RFC de integração](../rfcs/rfc-001-baseline-e-integracao.md).
- [Contratos](../contracts/README.md).
