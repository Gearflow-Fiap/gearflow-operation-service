# ADR-001: Fronteiras e Saga orquestrada

**Status:** Proposed. **Date:** 2026-10-24. **Decided by:** baseline local solicitada; revisão coletiva pendente. **Implementation:** parcial ou pendente conforme README.

## Context

O Workshop do monólito mistura orçamento, estoque e reparos. Budget/Inventory descrevem uma Saga no OS; Inventory já implementa comandos e eventos via RabbitMQ/MassTransit.

## Decision

Adotar como arquitetura de referência quatro serviços: OS, Operation, Budget e Inventory. OS mantém o processo global e a Saga persistida. Participantes decidem suas próprias regras locais, executam comandos e publicam fatos. Operation não cobra nem movimenta estoque. REST é reservado à interação humana e consultas necessárias; comandos entre serviços seguem pelo broker.

**Why:** Centralizar o fluxo distribuído no OS torna etapas, compensações e demonstração observáveis sem espalhar a coordenação entre participantes.

## Consequences

### Positive

Fronteiras explícitas e aproveitamento do Inventory existente.

### Negative

OS passa a concentrar a coordenação; exige persistência, concorrência e testes da Saga.

### Mitigations

Reinício seguro via estado persistido, outbox, testes de falha e métricas de Sagas paradas.

## Alternatives Considered

Coreografia: possível, mas aumenta dispersão do fluxo nesta equipe. Orquestrador separado: adiciona deploy sem necessidade demonstrada. Copiar todo Workshop: mantém responsabilidades e dados acoplados.

## Related

- [Visão geral](OVERVIEW.md).
- [Saga](SAGA.md).
- [RFC de integração](../rfcs/rfc-001-baseline-e-integracao.md).
- [Contratos](../contracts/README.md).
