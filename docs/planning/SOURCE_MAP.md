# Fontes e limites da baseline

Data: 2026-10-24. Snapshot documental, não aprovação coletiva.

| Fonte | Evidência usada |
|---|---|
| PDF SOAT - Fase 4 - Tech challenge, páginas 2-6 | Requisitos de microsserviços, Saga, bancos, testes, CI/CD e entregáveis; análise original em FASE-4-ANALISE.md |
| gearflow-app/docs/INDEX.md e templates ADR/RFC | Estrutura de documentação replicada |
| gearflow-app/Directory.Build.props | Runtime .NET 10 atual |
| Workshop.Domain/ServiceOrder e Application handlers | Regras e acoplamentos a separar |
| Workshop.Infrastructure/CrossBc/SqlPricingReader | Preços atuais por leitura SQL cruzada |
| Workshop.Domain/BudgetModel/Budget | Soma atual de insumos sem multiplicação explícita de quantidade |
| Catalog.Domain/Aggregates/Job | Modelo de mão de obra candidato à extração |
| gearflow-app/docs/architecture/adr-003-authentication-strategy.md | JWT, distinção de atores e revogação dependente de Identity |
| gearflow-app/docs/architecture/OBSERVABILITY.md | Padrões de observabilidade da fase 3 |
| gearflow-budget-service/README.md e docs/fluxo-saga.md | Propostas de Saga/fluxo comercial/MongoDB e divergências de estoque |
| InventoryMessages.cs e QueueNames.cs do Inventory | Contratos e filas já implementados; snapshot SHA no guia de compatibilidade |
| Inventory MessagingServiceCollectionExtensions | Configuração concreta de RabbitMQ/MassTransit/outbox/inbox |
| Inventory ReserveStockCommandValidator | Reserva não vazia e IDs de estoque únicos |
| Inventory PartDto/ConsumableDto e endpoints | Campos e rotas de consulta de preços existentes |
| Inventory ADR-0002 e ADR-0003 | Idempotência/tombstone e proposta de consumo comandado pela Saga |
| README e código núcleo Operation | Estado real de implementação e diferenças para o alvo |

Não inspecionados nesta etapa: infraestrutura, frontend, Lambda e OS completo. Não executados: serviços distribuídos, testes Inventory/Budget, Swagger, deploy ou publicação de pacote. Schema/example validation não comprova compatibilidade wire MassTransit.
