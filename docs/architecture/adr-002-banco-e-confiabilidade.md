# ADR-002: Banco exclusivo e entrega confiável

**Status:** Proposed. **Date:** 2026-10-24. **Decided by:** baseline local solicitada; revisão coletiva pendente. **Implementation:** parcial ou pendente conforme README.

## Context

Operation precisa persistir estado, itens, histórico, deduplicação e mensagens. Inventory já utiliza PostgreSQL, EF e outbox/inbox; monólito usa SQL Server.

## Decision

Propor PostgreSQL exclusivo do Operation com EF Core, migrations próprias, chave única por ServiceOrderId e token de concorrência. Gravar agregado, histórico, intenção HTTP deduplicada e outbox na mesma transação. Consumidores usam inbox e idempotência de negócio. Não usar cache/memória como fonte durável. Credenciais e migrations não acessam bases de outros serviços.

**Why:** Reutilizar o padrão implementado no Inventory reduz divergência operacional; transações locais e outbox impedem o intervalo salvar-sem-avisar.

## Consequences

### Positive

Histórico, recuperação e concorrência auditáveis.

### Negative

Mais tabelas e manutenção da outbox; requer banco/broker reais nos testes.

### Mitigations

Índices únicos, retry delimitado, limpeza com retenção definida, alerta de backlog e migrations como etapa controlada antes do rollout.

## Alternatives Considered

SQL Server próprio: tecnicamente válido, alternativa se infraestrutura exigir. MongoDB: já planejado no Budget e sem vantagem clara para esse estado relacional. Banco compartilhado: viola autonomia. Publicar depois do Save sem outbox: perde mensagens em falha.

## Related

- [Visão geral](OVERVIEW.md).
- [Saga](SAGA.md).
- [RFC de integração](../rfcs/rfc-001-baseline-e-integracao.md).
- [Contratos](../contracts/README.md).
