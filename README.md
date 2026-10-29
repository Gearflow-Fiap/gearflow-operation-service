# GearFlow Operation Service

Microsserviço de diagnóstico e execução da oficina, extraído do Workshop do gearflow-app para a fase 4 da FIAP.

## Status real e arquitetura de referência

**Implementado:** núcleo Domain/Application .NET 10, estados herdados do monólito, histórico, progresso, fila, porta de unidade de trabalho e 19 testes. Build/testes e cobertura inicial foram executados localmente; CI está preparado, sem execução remota verificada.

**Planejado:** catálogo de mão de obra, diagnóstico com itens/preços, fila autorizada distinta de reparo iniciado, API autenticada, PostgreSQL próprio, EF/migrations, RabbitMQ/MassTransit, inbox/outbox, simulador, BDD, OpenTelemetry, Docker/Kubernetes, Sonar e deploy.

**Contrato em revisão:** a especificação v1 em docs/contracts é uma baseline proposta para os integrantes. Não é um pacote publicado nem garantia de compatibilidade com outros serviços. As dependências e divergências estão na RFC-001. Implementação atual e desenho alvo não são equivalentes; veja o plano de migração.

## Decisões de referência

| Tema | Direção | Estado |
|---|---|---|
| Fronteiras | Operation controla diagnóstico/reparos; OS controla estado global/Saga; Budget cobra; Inventory movimenta estoque | Adotada como baseline local; revisar no grupo |
| Coordenação | Saga orquestrada no OS Service, por RabbitMQ | Proposed, consistente com os documentos Budget/Inventory |
| Tecnologia Operation | .NET 10, PostgreSQL exclusivo, EF Core e MassTransit na linha 8.5 usada pelo Inventory | Proposed; runtime já existe |
| Catálogo | Mão de obra no Operation; peças/insumos no Inventory; orçamento guarda snapshot | Proposed |
| Execução | StartExecution autoriza fila; ação autenticada inicia reparo físico | Proposed; exige evolução do núcleo |
| Conclusão | ExecutionFinished -> ConsumeStock -> StockConsumed -> OS Finalized | Proposed; coincide com ADR-0003 do Inventory |
| Falhas | Cancelamento confirmado pelo Operation antes de liberar reserva; após início físico, recuperação | Proposed |

ADRs registram contexto, alternativas e consequências. Nenhuma aprovação dos outros integrantes foi presumida.

## Execução acompanhada — passos 1 a 3

O trabalho segue checkpoints por passo, com evidência de testes. Consulte o [acompanhamento](docs/planning/EXECUTION_TRACKER.md), a [matriz de paridade da fase 3](docs/planning/STEP_1_PARITY.md) e os [critérios de aceite](docs/planning/STEP_ACCEPTANCE.md). O passo 1 fecha inventário/escopo; não implementa o modelo dos passos 2/3. Os contratos propostos anteriormente ainda precisam dos ajustes de paridade registrados na matriz.

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

## Executar o que existe hoje

Requer SDK .NET 10; não requer banco/broker porque só há núcleo local.

```powershell
dotnet restore GearFlow.Operation.slnx
dotnet test GearFlow.Operation.slnx --no-restore --collect:"XPlat Code Coverage" --results-directory TestResults
```

No ambiente restrito usado na primeira execução, foi necessário DOTNET_CLI_HOME gravável e `-m:1 -nr:false`. O VSTest precisa de comunicação local entre processos.

[Validação inicial](docs/coverage/README.md): 19 testes passaram; 101/101 linhas e 44/44 branches instrumentados. São somente Domain/Application existentes; não comprovam API, banco, broker ou BDD distribuído.

## Estrutura

```text
src/
  Operation.Domain/          # implementado
  Operation.Application/     # implementado
tests/
  Operation.Tests/           # implementado
docs/
  architecture/              # visão, diagramas, Saga e ADRs
  contracts/                 # especificação, schemas e exemplos v1 propostos
  development/               # API, qualidade e operação
  planning/                  # sequência de implementação e critérios de aceite
  rfcs/                      # propostas para revisão do grupo
```

Operation.Infrastructure, Operation.Api e o pacote de contratos serão adicionados nos incrementos previstos. OperationEvent atual é interno e provisório: não deve ser publicado como contrato RabbitMQ.
