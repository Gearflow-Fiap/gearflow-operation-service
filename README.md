# GearFlow Operation Service

Microsserviço de diagnóstico e execução extraído do gearflow-app para a fase 4 da FIAP.

## Executar no Visual Studio

Abra `GearFlow.Operation.slnx`, defina **docker-compose** como Startup Project e execute o perfil **Docker Compose** (F5/Ctrl+F5), com Docker Desktop Linux ativo. O Scalar abre em http://localhost:5085/scalar/v1; o health check continua em /health/ready. Credenciais locais são inicializadas apenas quando ausentes, sem substituir valores existentes. [Guia](docs/development/VISUAL_STUDIO.md).

CLI/CI: use `GearFlow.Operation.CI.slnf` para build/test de todos os projetos .NET sem o SDK Docker da IDE. [Estrutura de pastas](docs/development/PROJECT_STRUCTURE.md) e [testes por camada](tests/README.md).

## Estado atual

**Organização M1–M5 concluída localmente.** A avaliação M5 mantém casos de uso explícitos e pipeline único; [ADR-014](docs/architecture/adr-014-casos-de-uso-e-handlers.md) registra a decisão e critérios para reavaliar handlers/MediatR. [Mapa de casos de uso](docs/development/APPLICATION_USE_CASES.md), [evidências M5](docs/planning/evidence/quality-m5/RESULTS.md) e [plano](docs/planning/CODE_QUALITY_PLAN.md). No M5: build e 168 testes unitários reexecutados, sem mudança de produção. Última regressão completa: M4, 294 .NET + 13 Python aprovados, cobertura mínima 95,79%. Próximo foco: integração/entrega 7B.

Implementados localmente: domínio/casos de uso, PostgreSQL/EF/migrations, journal idempotente e concorrência, API JWT/Staff, catálogo próprio, adapter HTTP de preços Inventory, contratos tipados e consumers/outbox RabbitMQ/MassTransit.

**M1 — contrato HTTP validado:** 16 rotas documentadas, 236 testes .NET distintos e 13 testes Python aprovados; cobertura da API 95,31%. [Plano M1–M5](docs/planning/CODE_QUALITY_PLAN.md) e [evidências](docs/planning/evidence/quality-m1/RESULTS.md).

Incremento 7A acrescenta imagem/Compose, migration CLI, probes, logs JSON, traces/métricas OTLP e manifests/workflows. [Guia](docs/development/DELIVERY_AND_OBSERVABILITY.md), [Kubernetes](k8s/README.md), [ADR-012](docs/architecture/adr-012-observabilidade.md), [ADR-013](docs/architecture/adr-013-entrega-operation.md).

Pendente (7B): homologação com OS/Budget/Inventory, owner OS/Saga, contratos/topologia/credenciais, BDD distribuído, PostgreSQL/telemetria/Sonar e deploy remotos. Identidade/claims dependem do emissor. Testes locais não encerram a fase 4.

**7B.1 — kit para revisão do grupo:** [guia de integração](docs/integration/README.md), matriz de compatibilidade, decisões pendentes e cenários conjuntos. Gerador de ZIP validado por oito testes Python; CI prepara o artifact com contratos/exemplos e NuGet, sem publicação. O dono da OS/Saga continua sem confirmação do grupo. [Evidências](docs/planning/evidence/increment-7b1/RESULTS.md).

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

- [Docker, entrega e observabilidade](docs/development/DELIVERY_AND_OBSERVABILITY.md).
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
  Operation.UnitTests/
  Operation.IntegrationTests/
tools/
  Operation.ContractArtifacts/
  Operation.Migrate/
docs/
  architecture/
  contracts/
  development/
  planning/
  rfcs/
```

O slnx organiza projetos em src/tests/tools. GearFlow.Contracts.Operation 1.0.0-preview.1 foi empacotado localmente; não publicado em registry. Mudanças desta etapa permaneceram no Operation; demais serviços e infra-broker não foram alterados.
