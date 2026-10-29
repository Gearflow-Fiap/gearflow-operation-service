# Incremento 7A — fundações de entrega validadas

Checkpoint: 29/10/2026. **225 testes distintos aprovados**: 161 unitários e 64 integração; zero falhas/ignorados. Duas regressões também executadas separadamente e aprovadas (não somadas novamente). Build Release sem avisos/erros.

## Implementado

- Dockerfile .NET 10 multi-stage, API/migrator separados, execução sem root e Compose local com PostgreSQL 17, filesystem somente leitura na API e credenciais ignoradas.
- Ferramenta --check/--apply e quatro migrations; histórico EF fixado em public, com teste de reaplicação sob search_path=operation,public. [Diagnóstico da reexecução](migration-restart-diagnosis.md).
- Liveness/readiness, logs JSON, correlação W3C HTTP/comandos/outbox/inbox, métricas de comandos/entregas e backlog SQL. Exportação OTLP opcional.
- Kustomize isolado no namespace gearflow, Job de migration, probes, secrets próprios, overlay Kong opcional e referência de imagem por digest.
- CI preparada para testes/contratos/cobertura, smoke Docker e renderização; Sonar opcional configurável. Workflow de publicação/deploy desativado por flags até configuração remota.
- ADR-012/013, guia de execução/observabilidade, README e plano de retomada.

## Validação

PostgreSQL 17.11 nativo real e RabbitMQ 4.1 real (broker de teste dedicado). Coleção de banco usa somente databases operation_it_<guid> criados pelo fixture; servidor nativo final encerrado de forma controlada. Testes comprovam readiness/schema, reaplicação da ferramenta, trace HTTP/erro/outbox e publicação real com continuidade de trace. Coletor OTLP HTTP/protobuf local recebeu traces, métricas e logs com header configurado; não é prova de ingestão no New Relic.

| Assembly | Linhas | Cobertura |
|---|---:|---:|
| Contracts | 26/26 | 100,00% |
| API | 303/324 | 93,52% |
| Application | 345/345 | 100,00% |
| Domain | 370/374 | 98,93% |
| Infrastructure | 2511/2532 | 99,17% |

Gate >=80% por assembly aprovado. [Cobertura](coverage.txt), [contagens](tests.json). TRX e relatórios Cobertura/OpenCover em TestResults/increment-7a-verified (ignorados); não confundir com diretórios das tentativas anteriores, que registram falhas corrigidas.

Os 18 contratos foram exportados do CLR e comparados aos schemas/exemplos existentes: validação aprovada, sem mudança de contrato público. Manifests base/Kong e overlay de digest renderizados offline; [verificações estáticas](kubernetes-static.txt). YAML dos workflows/Compose analisado; execução remota não realizada.

Imagem final reconstruída pelo usuário: migrate Exited(0), API healthy, PostgreSQL healthy. Agente repetiu smoke HTTP sobre o container: live/ready 200, anônimo 401, cliente 403, criação 201, consulta/desativação 200. [Evidência JSON](docker-smoke.json). Nenhum volume foi apagado. O agente não teve acesso ao pipe Docker; build e status foram executados pelo usuário, smoke HTTP pelo agente.

## Limites e retomada 7B

Infraestrutura fase 3 inspecionada: EKS/Kong usa namespace gearflow; banco provisionado é SQL Server do monólito. Nenhum outro repositório foi alterado. Operation precisa de PostgreSQL próprio e credenciais/rede/secrets adequados.

Permanecem pendentes: definição coletiva OS/Saga, envelope/topologia e contratos/credenciais, integração real Inventory/Budget/OS e BDD/compensações; provisionamento PostgreSQL e secrets; execução remota CI/Sonar, publicação GHCR, proteção de environment/branch, OIDC, deploy e validação do backend de telemetria/alertas. Nenhum pacote/imagem publicado ou recurso cloud aplicado. Este checkpoint conclui 7A local e não a fase 4 completa.
