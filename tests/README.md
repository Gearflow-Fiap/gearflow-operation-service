# Testes do Operation

## Organização

| Pasta | O que verifica | Dependências reais |
|---|---|---|
| Operation.UnitTests/Domain | Recebimento, diagnóstico, snapshots, execução, cancelamento e cópias destacadas | Nenhuma |
| Operation.UnitTests/Application | Intenções, consultas, fila, preço, telemetria e casos de uso | Doubles em memória |
| Operation.UnitTests/Infrastructure | Mapper de persistência e cliente HTTP de preços | Servidor/handler substituto; sem PostgreSQL |
| Operation.UnitTests/Contracts | Projeção e serialização dos contratos públicos | Serializadores reais; sem broker |
| Operation.UnitTests/Architecture | Dependências permitidas entre assemblies/camadas | Metadata dos assemblies |
| Operation.UnitTests/Support | Dados de domínio, relógio e storage de teste compartilhados | Não são adapters de produção |
| Operation.IntegrationTests/Api | JWT, endpoints, catálogo/preços e readiness/observabilidade | PostgreSQL e hosts HTTP de teste |
| Operation.IntegrationTests/Persistence | Transações, constraints, migrations, idempotência e concorrência | PostgreSQL 17 |
| Operation.IntegrationTests/Messaging | Comandos/eventos, inbox/outbox, duplicatas, erro e recuperação | PostgreSQL e RabbitMQ |
| Operation.IntegrationTests/Delivery | CLI de migration, trace no broker e exportação OTLP | PostgreSQL, RabbitMQ e coletor local |
| Operation.IntegrationTests/Support | ApiFactory, PostgresFixture/IntegrationHarness e BrokerHarness | Infraestrutura isolada de teste |
| python | Kit, inicialização idempotente e override do debugger após arquivo gerado pela IDE | Python, PowerShell e Docker Compose CLI (sem daemon no teste de configuração) |

Pasta e namespace dos cenários seguem a camada. No Test Explorer, agrupar por Namespace/Class para distinguir o que está sendo testado. Fixtures não ficam misturadas aos cenários. Pacotes comuns de teste ficam em tests/Directory.Build.props.

## Comandos

```powershell
# Sem Docker ou banco:
dotnet test tests/Operation.UnitTests -c Release
dotnet test tests/Operation.UnitTests -c Release --filter FullyQualifiedName~Operation.UnitTests.Domain
dotnet test tests/Operation.UnitTests -c Release --filter FullyQualifiedName~Operation.UnitTests.Application

# Integração: PostgreSQL 17 via Testcontainers ou OPERATION_TEST_POSTGRES.
docker compose -f compose.messaging-tests.yml up -d --wait
dotnet test tests/Operation.IntegrationTests -c Release

# Toda a solução .NET, sem SDK Docker do Visual Studio:
dotnet test GearFlow.Operation.CI.slnf -c Release --collect:"XPlat Code Coverage" --settings coverage.runsettings --results-directory TestResults/current
python scripts/check_coverage.py TestResults/current
python -m unittest discover -s tests/python -v
```

O filtro CI.slnf contém todos os projetos .NET da solução; apenas o .dcproj do Visual Studio fica fora. Não exclui testes ou camadas do gate de cobertura. Testes de integração continuam serializados na coleção Postgres; cada caso usa banco exclusivo, e testes de broker usam vhost exclusivo. Não apontar o fixture para banco/vhost compartilhados de homologação.

Mudar a localização dos cenários não os transforma em BDD distribuído nem em prova de serviços externos completos. As evidências de cada checkpoint ficam em docs/planning/evidence.
