# Documentação GearFlow Operation

Data da baseline: 2026-10-24. Esta é a proposta de referência para planejar a fase 4; o status de implementação está no README. O material não registra concordância de integrantes que ainda não revisaram.

## Comece por aqui

| Objetivo | Documento |
|---|---|
| Entender fronteiras e dono dos dados | [OVERVIEW](architecture/OVERVIEW.md) |
| Visualizar componentes e fluxos | [Diagramas](architecture/ARCHITECTURE_DIAGRAMS.md) |
| Entender compensações e concorrência | [Saga](architecture/SAGA.md) |
| Implementar integração com Operation | [Contratos v1](contracts/README.md) |
| Integrar frontend | [API proposta](development/API_ENDPOINTS.md) |
| Planejar entregas e validar o PDF | [Plano](planning/IMPLEMENTATION_PLAN.md) |
| Revisar com os outros responsáveis | [RFC-001](rfcs/rfc-001-baseline-e-integracao.md) |
| Discutir identidade/clientes/preços | [RFC-002](rfcs/rfc-002-catalogo-identidade-e-precos.md) |
| Publicar e versionar contratos | [RFC-003](rfcs/rfc-003-distribuicao-de-contratos.md) |
| Consultar observabilidade | [Operação](development/OBSERVABILITY.md) |
| Ver testes e critérios de cobertura | [Qualidade](development/TEST_STRATEGY.md) |
| Consultar análise original | [FASE-4-ANALISE](FASE-4-ANALISE.md) |

## ADRs

ADRs 001–005 continuam Proposed até revisão coletiva. ADRs 006/007 registram as escolhas locais do modelo e da aplicação; não equivale à adoção dos contratos em outros repositórios.

| ADR | Assunto |
|---|---|
| [001](architecture/adr-001-fronteiras-e-saga.md) | Fronteiras, Saga orquestrada e infraestrutura de comunicação |
| [002](architecture/adr-002-banco-e-confiabilidade.md) | PostgreSQL exclusivo, inbox/outbox e concorrência |
| [003](architecture/adr-003-catalogo-e-snapshots.md) | Catálogo de mão de obra, preços e snapshots |
| [004](architecture/adr-004-execucao-e-compensacao.md) | Autorização, início físico, conclusão e cancelamento |
| [005](architecture/adr-005-contratos-e-versionamento.md) | Identidade de mensagens, contratos e evolução |
| [006](architecture/adr-006-modelo-local-e-precificacao.md) | Modelo local e limite da precificação no passo 2 |
| [007](architecture/adr-007-casos-de-uso-e-intencoes.md) | Intenções, casos de uso e protocolo de gravação local |

## Governança

RFC = discussão transversal com responsáveis e questões abertas. ADR = registro de uma escolha arquitetural e seus efeitos. Ao aceitar, preencher data e nomes/papéis dos revisores; registrar PRs/commits dos serviços afetados. Não renumerar nem apagar ADR antigo: marcar Superseded e apontar o sucessor.

Schemas e exemplos em contracts/v1 definem o corpo lógico das mensagens novas do Operation, não o envelope de transporte MassTransit. O código atual não os implementa ainda. Os contratos Inventory existentes continuam intactos.

## Evidências e rastreabilidade

- [Fontes inspecionadas e limites](planning/SOURCE_MAP.md).
- [Validação documental da baseline](planning/VALIDATION.md).

## Execução atual: passos 1 a 3

- [Ponto de retomada e checkpoints](planning/EXECUTION_TRACKER.md).
- [Passo 1: matriz de paridade e escopo](planning/STEP_1_PARITY.md).
- [Manifesto de fontes e casos mapeados](planning/PARITY_MANIFEST.json).
- [Critérios de qualidade por passo](planning/STEP_ACCEPTANCE.md).

Esta sequência local não é a mesma numeração P0-P8 do plano geral. O acompanhamento registra a conclusão de cada passo e a parada solicitada antes de prosseguir.

- [Passo 2: modelo operacional implementado e limites](planning/STEP_2_MODEL.md).

- [Passo 3: aplicação, interfaces e limites](planning/STEP_3_APPLICATION.md).

- [Persistência PostgreSQL](architecture/adr-008-persistencia-postgresql.md) e [execução local/CI](development/POSTGRESQL.md).
