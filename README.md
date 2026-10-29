# GearFlow Operation Service

Microsserviço de diagnóstico e execução da oficina, extraído do Workshop do gearflow-app para a fase 4 da FIAP.

## Status real e arquitetura de referência

**Implementado:** domínio e casos de uso locais .NET 10, precificação por interface com política obrigatória, snapshot imutável, mapeamento de linhas do orçamento, comandos idempotentes por intenção, contrato de commit com CAS/receipt/eventos, consultas/DTOs/paginação e duração por fase. São 120 testes locais. O storage isolado e o catálogo simulados existem apenas nos testes; não há adapter de produção.

**Planejado:** PostgreSQL/EF e migrations, journal/outbox/inbox persistentes, API autenticada, catálogo e clients de preço reais, contratos wire/RabbitMQ, Saga/BDD integrado, OpenTelemetry, Docker/Kubernetes, Sonar e deploy. Políticas LegacyLinePriceV1 e PerUnitV1 estão implementadas sem default de produção; a escolha deve ser acordada com Budget.

**Contrato em revisão:** a especificação v1 em docs/contracts é uma baseline proposta para os integrantes. Não é um pacote publicado nem garantia de compatibilidade com outros serviços. As dependências e divergências estão na RFC-001. Implementação atual e desenho alvo não são equivalentes; veja o plano de migração.

## Decisões de referência

| Tema | Direção | Estado |
|---|---|---|
| Fronteiras | Operation controla diagnóstico/reparos; OS controla estado global/Saga; Budget cobra; Inventory movimenta estoque | Adotada como baseline local; revisar no grupo |
| Coordenação | Saga orquestrada no OS Service, por RabbitMQ | Proposed, consistente com os documentos Budget/Inventory |
| Tecnologia Operation | .NET 10, PostgreSQL exclusivo, EF Core e MassTransit na linha 8.5 usada pelo Inventory | Proposed; runtime já existe |
| Catálogo | Mão de obra no Operation; peças/insumos no Inventory; orçamento guarda snapshot | Proposed |
| Execução | StartExecution autoriza fila; ação autenticada inicia reparo físico | Implementado no domínio local; integração Proposed |
| Conclusão | ExecutionFinished -> ConsumeStock -> StockConsumed -> OS Finalized | Proposed; coincide com ADR-0003 do Inventory |
| Falhas | Cancelamento confirmado pelo Operation antes de liberar reserva; após início físico, recuperação | Proposed |

ADRs registram contexto, alternativas e consequências. Nenhuma aprovação dos outros integrantes foi presumida.

## Execução acompanhada — passos 1 a 3

O trabalho segue checkpoints por passo, com evidência de testes. Consulte o [acompanhamento](docs/planning/EXECUTION_TRACKER.md), a [matriz de paridade da fase 3](docs/planning/STEP_1_PARITY.md) e os [critérios de aceite](docs/planning/STEP_ACCEPTANCE.md). Os três passos locais foram concluídos em checkpoints separados; o próximo incremento são adapters de persistência reais. Os contratos propostos anteriormente ainda precisam dos ajustes de paridade registrados na matriz.

## Navegação

- [Índice da documentação](docs/INDEX.md).
- [Visão geral e fronteiras](docs/architecture/OVERVIEW.md).
- [Diagramas de componentes, sequência e estados](docs/architecture/ARCHITECTURE_DIAGRAMS.md).
- [Saga, falhas e compensações](docs/architecture/SAGA.md).
- [Contratos de integração v1](docs/contracts/README.md).
- [API HTTP proposta](docs/development/API_ENDPOINTS.md).
- [Plano de implementação e dependências](docs/planning/IMPLEMENTATION_PLAN.md).
- [Decisões a revisar com cada integrante](docs/rfcs/rfc-001-baseline-e-integracao.md).
- [Análise inicial do PDF e legado](docs/FASE-4-ANALISE.md) (registro histórico).

## Último checkpoint

Incremento 4 concluído: persistência PostgreSQL/EF, migrations, histórico, receipts idempotentes, concorrência e outbox interna transacional. **144 testes aprovados** (130 unitários e 14 de integração no PostgreSQL real), sem falhas/ignorados. Cobertura consolidada: Application 100%, Domain 98,93%, Infrastructure 98,50% (inclui migrations geradas).

- [Evidências](docs/planning/evidence/increment-4/RESULTS.md).
- [Decisão de persistência](docs/architecture/adr-008-persistencia-postgresql.md).
- [Execução local e CI](docs/development/POSTGRESQL.md).
- [Próximos incrementos](docs/planning/NEXT_INCREMENTS.md) e [acompanhamento](docs/planning/EXECUTION_TRACKER.md).

## Estrutura

```text
src/
  Operation.Domain/
  Operation.Application/
  Operation.Infrastructure/
tests/
  Operation.Tests/
  Operation.IntegrationTests/
docs/
  architecture/
  contracts/
  development/
  planning/
  rfcs/
```

O slnx organiza projetos nas pastas src e tests. Requer SDK .NET 10; integração requer PostgreSQL 17, via Docker/Testcontainers ou OPERATION_TEST_POSTGRES. Consulte o guia acima para comandos e migrations.

Ainda faltam host HTTP/JWT, catálogo real, contratos homologados, RabbitMQ/MassTransit, dispatcher da outbox e integração da Saga. OperationEvent é registro interno persistido, não um contrato RabbitMQ já publicado. Os outros serviços e gearflow-app não foram alterados neste incremento.
