# GearFlow Operation Service

Microsserviço de diagnóstico e execução extraído do gearflow-app para a fase 4 da FIAP.

## Docker local integrado com a OS

Com Docker Desktop Linux iniciado, pare as APIs que estiverem rodando por `dotnet run`
(Ctrl+C) para liberar as portas. Não é necessário definir variáveis no terminal:

```powershell
cd C:\WS\gearflow-os-service
docker compose up -d --build
cd C:\WS\gearflow-operation-service
docker compose up -d --build
```

O Compose padrão (`compose.yml`) inicia PostgreSQL na porta 5434, aplica migrations
automaticamente e sobe a API em http://localhost:5085. Mensageria, despacho, claims
Staff e Pricing__ConsumableMode=PerUnitV1 já estão habilitados. O JWT local usa
GearFlow.Api/GearFlow.Client e o mesmo segredo de desenvolvimento da OS.

A OS cria a rede `gearflow-local-integration` e mantém o único RabbitMQ; o Operation
usa o DNS `rabbitmq` nessa rede. Subir a OS primeiro é obrigatório. Cada PostgreSQL
fica na rede privada do próprio projeto; os volumes existentes são preservados.
Somente portas no loopback são expostas. Defaults são exclusivos de desenvolvimento.

```powershell
docker compose ps
docker compose logs --tail 100 api migrate
curl.exe http://localhost:5085/health/ready
```

O perfil isolado/Visual Studio continua em `compose.local.yml`, com configuração
própria. Para a integração use o Compose padrão acima. Inventory/Billing não estão
incluídos; o teste de diagnóstico apenas com mão de obra funciona sem eles.
Preços de peças/consumíveis exigem OPERATION_INVENTORY_URL/TOKEN/HTTP, configuráveis
num `.env` local opcional. Nunca remover volumes para reiniciar o teste.

Com os dois serviços prontos, execute no PowerShell:

```powershell
. .\scripts\test-os-operation-local.ps1 -Scenario DiagnosticFailure
```

O cenário cria dados novos, registra uma falha de diagnóstico e verifica, por até
30 segundos, a convergência OS=Cancelled, Operation=Canceled/inativa e confirmação
do cancelamento no histórico da OS. Preserva o motivo da falha e exige a razão
`diagnostic-failed` no Operation. Usa HTTP e RabbitMQ reais; não injeta eventos
de desenvolvimento. Sem pagamento ou peças, não valida estorno/liberação de estoque.
Sem `-Scenario`, mantém o teste de diagnóstico concluído.

Para testar recuperacao (executar um cenario por vez, sem outros testes simultaneos):

```powershell
. .\scripts\test-os-operation-local.ps1 -Scenario BrokerRecovery
. .\scripts\test-os-operation-local.ps1 -Scenario OperationRecovery
. .\scripts\test-os-operation-local.ps1 -Scenario OsRecovery
```

BrokerRecovery para temporariamente o RabbitMQ, abre uma OS e verifica o comando
pendente na outbox antes de religar o broker. OperationRecovery para apenas a API
Operation, publica o comando e religa o consumidor. Ambos usam `try/finally` para
tentar restaurar o container mesmo em falha; aguardam reconexao sem reiniciar as
APIs restantes. Verificam ausencia da operacao antes de recuperar, uma operacao
com versao/historico inicial unicos depois e retorno do diagnostico a OS. Cada
espera de prontidao tem ate 120 tentativas, com timeout HTTP de 3s por tentativa.
Esses testes usam Docker Compose e consultas somente de leitura nos bancos locais
para conferir resultados; os servicos nao passam a acessar bancos alheios.
Use apenas no ambiente local, pois o container indicado fica indisponivel durante
o teste. Parametros OsComposeFile/OperationComposeFile permitem apontar outro
ambiente Compose de testes; suas credenciais de banco devem ser as do perfil local.
OsRecovery para apenas a API OS depois do inicio do diagnostico, conclui no
Operation e verifica a publicacao do evento antes de religar a OS. Confirma que o
snapshot so e persistido apos o consumidor voltar. Tambem restaura a API em finally.

## Executar no Visual Studio

Abra `GearFlow.Operation.slnx`, defina **docker-compose** como Startup Project e execute o perfil **Docker Compose** (F5/Ctrl+F5), com Docker Desktop Linux ativo. O Scalar abre em http://localhost:5085/scalar/v1; o health check continua em /health/ready. Credenciais locais são inicializadas apenas quando ausentes, sem substituir valores existentes. [Guia](docs/development/VISUAL_STUDIO.md).

CLI/CI: use `GearFlow.Operation.CI.slnf` para build/test de todos os projetos .NET sem o SDK Docker da IDE. [Estrutura de pastas](docs/development/PROJECT_STRUCTURE.md) e [testes por camada](tests/README.md).

## Estado atual

**Organização M1–M5 concluída localmente.** A avaliação M5 mantém casos de uso explícitos e pipeline único; [ADR-014](docs/architecture/adr-014-casos-de-uso-e-handlers.md) registra a decisão e critérios para reavaliar handlers/MediatR. [Mapa de casos de uso](docs/development/APPLICATION_USE_CASES.md), [evidências M5](docs/planning/evidence/quality-m5/RESULTS.md) e [plano](docs/planning/CODE_QUALITY_PLAN.md). No M5: build e 168 testes unitários reexecutados, sem mudança de produção. Última regressão completa: atualização dos exchanges de eventos, 297 .NET + 13 Python aprovados, cobertura mínima 95,79%. Próximo foco: integração/entrega 7B.

Implementados localmente: domínio/casos de uso, PostgreSQL/EF/migrations, journal idempotente e concorrência, API JWT/Staff, catálogo próprio, adapter HTTP de preços Inventory, contratos tipados e consumers/outbox RabbitMQ/MassTransit.

**M1 — contrato HTTP validado:** 16 rotas documentadas, 236 testes .NET distintos e 13 testes Python aprovados; cobertura da API 95,31%. [Plano M1–M5](docs/planning/CODE_QUALITY_PLAN.md) e [evidências](docs/planning/evidence/quality-m1/RESULTS.md).

Incremento 7A acrescenta imagem/Compose, migration CLI, probes, logs JSON, traces/métricas OTLP e manifests/workflows. [Guia](docs/development/DELIVERY_AND_OBSERVABILITY.md), [Kubernetes](k8s/README.md), [ADR-012](docs/architecture/adr-012-observabilidade.md), [ADR-013](docs/architecture/adr-013-entrega-operation.md).

Pendente (7B): homologação com OS/Budget/Inventory, owner OS/Saga, contratos/topologia/credenciais, BDD distribuído, PostgreSQL/telemetria/Sonar e deploy remotos. Identidade/claims dependem do emissor. Testes locais não encerram a fase 4.

**7B.1 — kit para revisão do grupo:** [guia de integração](docs/integration/README.md), matriz de compatibilidade, decisões pendentes e cenários conjuntos. Gerador de ZIP validado por oito testes Python; CI prepara o artifact com contratos/exemplos e NuGet, sem publicação. O dono da OS/Saga continua sem confirmação do grupo. [Evidências](docs/planning/evidence/increment-7b1/RESULTS.md).

**Eventos publicados:** 13 exchanges `operation.<fato>` sem sufixo `.v1`, como `operation.diagnostic-finished` e `operation.execution-finished`. Contratos/URN e filas de comandos preservados. Consumidores devem configurar os mesmos nomes e seus bindings antes de habilitar o dispatcher. [Mapa e transição](docs/development/MESSAGING.md#exchanges-dos-eventos-publicados), [evidências](docs/planning/evidence/event-exchanges/RESULTS.md).

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
