# GearFlow Operation Service

Microsserviço de diagnóstico e execução extraído do gearflow-app para a fase 4 da FIAP.

## Estado atual

Implementados localmente: domínio/casos de uso, PostgreSQL/EF/migrations, journal idempotente e concorrência, API JWT/Staff, catálogo próprio, adapter HTTP de preços Inventory, contratos tipados e consumers/outbox RabbitMQ/MassTransit.

**Incremento 6 validado:** 215 testes distintos aprovados (159 unitários + 56 integração), sem falhas/ignorados. PostgreSQL 17.11 e RabbitMQ 4.1 reais; esquemas/exemplos dos 18 contratos validados e preview NuGet gerado. [Evidências](docs/planning/evidence/increment-6/RESULTS.md).

Pendente: homologação com OS/Budget/Inventory completos, decisão coletiva de envelope/topologia/owner dos contratos e credenciais, Saga/BDD distribuído, observabilidade e entrega Docker/K8s/Sonar/deploy. Identidade/claims reais também dependem do emissor. Testes locais do participante não equivalem à fase 4 completa.

## Responsabilidades

| Serviço | Dono |
|---|---|
| Operation | catálogo de mão de obra, diagnóstico, fila e execução física |
| Inventory | preços/saldo/reservas de peças e insumos |
| Budget | orçamento, aprovação e pagamento |
| OS (proposto) | pedido global e coordenação persistente da Saga |
| infra-broker | infraestrutura, topologia acordada, credenciais e operação RabbitMQ |

StartDiagnostic recebe o pedido; início humano é HTTP. StartExecution autoriza execução após decisões do coordenador; não inicia reparo nem verifica pagamento/estoque por conta própria. Operation não é a Saga financeira. Snapshots/identidades de linhas permanecem imutáveis; conclusão exige todos os serviços executados.

## Executar e configurar

Requer .NET 10 e banco exclusivo PostgreSQL 17. Migrations são tarefa explícita, não executadas no boot. Definir JWT/Staff, conexão e política de consumíveis por ambiente; sem segredos versionados.

- [PostgreSQL, migrations e testes](docs/development/POSTGRESQL.md).
- [API HTTP e configuração](docs/development/API_ENDPOINTS.md).
- [Catálogo e preços Inventory](docs/development/CATALOG_AND_PRICING.md).
- [RabbitMQ, consumers, outbox e recuperação](docs/development/MESSAGING.md).
- [Contratos executáveis, schemas e exemplos](docs/contracts/README.md).

Messaging:Enabled e Messaging:DispatchEnabled são flags explícitas; padrão desligado. Provisionar assinaturas duráveis e revisar pendências antes de ligar dispatcher. A topologia/envelope inicial do infra-broker difere do padrão MassTransit usado aqui e no Inventory; nenhuma infraestrutura compartilhada foi alterada automaticamente.

## Qualidade e checkpoints

[Acompanhamento](docs/planning/EXECUTION_TRACKER.md), [próximos incrementos](docs/planning/NEXT_INCREMENTS.md) e [índice](docs/INDEX.md). Registros dos passos anteriores são históricos; o índice ativo de contratos é runtime-v1, não a proposta antiga da pasta v1.

Cobertura consolidada por assembly >=80%, com exclusão restrita ao arquivo gerado de biblioteca OpenAPI; migrations/modelos gerados continuam medidos. CI configura PostgreSQL/RabbitMQ, testa código e contratos, compara exportação CLR e empacota preview. Execução remota da CI/deploy não foi realizada neste checkpoint.

Reentrega pode duplicar mensagem com mesmo ID; não prometemos exactly-once. Inbox e journal evitam repetir efeito; outbox só marca entrega após confirmação/rota no broker. Schema, corpo, envelope e idempotência têm testes próprios.

## Estrutura

```text
src/
  Operation.Domain/
  Operation.Application/
  Operation.Infrastructure/
  Operation.Api/
  Operation.Contracts/
tests/
  Operation.Tests/
  Operation.IntegrationTests/
tools/
  Operation.ContractArtifacts/
docs/
  architecture/
  contracts/
  development/
  planning/
  rfcs/
```

O slnx organiza projetos em src/tests/tools. GearFlow.Contracts.Operation 1.0.0-preview.1 foi empacotado localmente; não publicado em registry. Mudanças desta etapa permaneceram no Operation; demais serviços e infra-broker não foram alterados.
