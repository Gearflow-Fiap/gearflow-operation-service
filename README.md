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

O trabalho segue checkpoints por passo, com evidência de testes. Consulte o [acompanhamento](docs/planning/EXECUTION_TRACKER.md), a [matriz de paridade da fase 3](docs/planning/STEP_1_PARITY.md) e os [critérios de aceite](docs/planning/STEP_ACCEPTANCE.md). Os três passos locais foram concluídos em checkpoints separados; persistência e API foram acrescentadas nos incrementos 4/5; as próximas entregas são mensageria e homologação entre serviços. Os contratos propostos anteriormente ainda precisam dos ajustes de paridade registrados na matriz.

## Navegação

- [Índice da documentação](docs/INDEX.md).
- [Visão geral e fronteiras](docs/architecture/OVERVIEW.md).
- [Diagramas de componentes, sequência e estados](docs/architecture/ARCHITECTURE_DIAGRAMS.md).
- [Saga, falhas e compensações](docs/architecture/SAGA.md).
- [Contratos de integração v1](docs/contracts/README.md).
- [API HTTP e configuração](docs/development/API_ENDPOINTS.md).
- [Plano de implementação e dependências](docs/planning/IMPLEMENTATION_PLAN.md).
- [Decisões a revisar com cada integrante](docs/rfcs/rfc-001-baseline-e-integracao.md).
- [Análise inicial do PDF e legado](docs/FASE-4-ANALISE.md) (registro histórico).

## Último checkpoint — incremento 5B

**198 testes aprovados** (155 unitários + 43 integração), sem falhas/ignorados. API 98,45%; Application 100%; Domain 98,93%; Infrastructure 98,87% (inclui migrations). [Evidências](docs/planning/evidence/increment-5b/RESULTS.md).

Catálogo de mão de obra PostgreSQL e adapter HTTP de preços Inventory implementados. CRUD autenticado, controle de versão/desativação, cotação com credencial própria e limites de tempo/resposta. Política financeira permanece explícita; para novos dados, proposta PerUnitV1. [Guia de configuração](docs/development/CATALOG_AND_PRICING.md) e [ADR-010](docs/architecture/adr-010-catalogo-e-precos.md).

Homologação com Inventory/Budget completos e credencial real ainda pendente; testes HTTP usam servidor substituto com DTOs do Inventory e banco Operation real. Próxima etapa: contratos/broker e integração distribuída. Registros dos checkpoints anteriores abaixo são históricos.
### Histórico — persistência

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
  Operation.Api/
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

Host HTTP/JWT implementado no 5A; consulte [API e configuração](docs/development/API_ENDPOINTS.md). Catálogo e adapter Inventory implementados; faltam homologação coletiva/credenciais reais, contratos públicos, RabbitMQ/MassTransit, dispatcher da outbox e integração da Saga. OperationEvent é registro interno persistido, não um contrato RabbitMQ já publicado. Os outros serviços e gearflow-app não foram alterados neste incremento.

## Padrão para alinhamento do grupo

[RFC-003 — padrão da fase 4](docs/rfcs/rfc-003-padrao-fase-4.md): responsabilidades, preços por unidade, snapshot, autenticação entre serviços e Saga. É uma recomendação para revisão coletiva; infra-broker provisiona mensageria e não substitui o serviço de OS/coordenador.

O incremento 5 foi dividido em 5A (HTTP) e 5B (catálogo/integração real de preços). No host atual, finalizar diagnóstico consulta o catálogo próprio e Inventory quando a configuração é fornecida; falta de configuração/cotação válida retorna 503. Testes do 5B usam transporte HTTP real com servidor substituto; não são evidência de deploy do Inventory completo.
