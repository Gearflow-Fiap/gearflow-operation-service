# GearFlow Operation Service

Microsserviço de diagnóstico e execução da oficina, extraído do Workshop do gearflow-app para a fase 4 da FIAP.

## Status real e arquitetura de referência

**Implementado:** modelo operacional .NET 10 com veículo, itens solicitados/diagnosticados, snapshots imutáveis, mapeamento de linhas do orçamento, autorização distinta de início físico, atividade/cancelamento, histórico e versão. São 72 testes locais de domínio e regressão do núcleo Application. CI está preparado, sem execução remota verificada.

**Planejado:** casos de uso completos, consultas/paginação, fonte/política de preços por portas, catálogo de mão de obra, API autenticada, PostgreSQL próprio, EF/migrations, RabbitMQ/MassTransit, inbox/outbox, simulador, BDD, OpenTelemetry, Docker/Kubernetes, Sonar e deploy. Snapshot de valores no domínio não equivale à consulta de preços real.

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

## Último checkpoint

Passo 2 concluído no modelo local; passo 3 não iniciado funcionalmente. [Modelo e limites](docs/planning/STEP_2_MODEL.md), [evidência](docs/planning/evidence/step-2/RESULTS.md) e [acompanhamento](docs/planning/EXECUTION_TRACKER.md). Resultado: 72 testes aprovados, 100% das 342 linhas e 150/153 branches (98,03%) no código instrumentado atual. O histórico do passo 1 permanece preservado.

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

Operation.Infrastructure, Operation.Api e o pacote de contratos serão adicionados nos incrementos previstos. A aplicação existente teve apenas assinaturas/fila adaptadas ao domínio; consultas, resolução de preços, unidade de trabalho isolada e intenções idempotentes ainda são trabalho do passo 3. OperationEvent atual é interno e provisório: não deve ser publicado como contrato RabbitMQ.
