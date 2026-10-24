# RFC-001: Baseline distribuída e responsabilidades de integração

**Status:** In Review (documento pronto para revisão, sem envio externo). **Author:** proposta preparada com o responsável Operation. **Date:** 2026-10-24. **Reviewers:** responsáveis OS, Budget, Inventory, Operation e infraestrutura, nomes a preencher.

## Summary

Quatro serviços e Saga no OS, reutilizando RabbitMQ/MassTransit e contratos Inventory. Este documento concentra as hipóteses para os responsáveis revisarem. Nenhuma tarefa foi atribuída ou mensagem enviada automaticamente aos integrantes.

## Motivation

Há implementação Inventory, documentação Budget e núcleo Operation, mas o desenho Budget ainda inclui estoque no Execution. É preciso fixar um fluxo e contratos coerentes antes dos consumers.

## Proposal

| Tema | Proposta | Quem revisa | Evidência de aceite |
|---|---|---|---|
| Orquestração | State machine persistida no OS; eventos não orquestram outros serviços | OS + todos | ADR/PR e cenários de Saga |
| Ordem comercial | Aprovar -> pagar -> reservar -> autorizar execução | Budget + OS + Inventory | Fluxo e teste de pagamento sem estoque |
| Conclusão | ExecutionFinished -> ConsumeStock -> StockConsumed -> Finalized; sem estoque, pular os dois passos | OS + Inventory | ADR-0003 conciliado e teste |
| Ponto sem volta | Início físico por HTTP, distinto de autorização da fila | Operation + OS | Máquina de estados e teste de corrida |
| Compensação | CancelOperation confirmado antes de release/refund; rejeição implica RecoveryRequired | Todos | Teste cancelamento x início |
| Snapshot | LineId e DiagnosticId estáveis, orçamento revisão 1 imutável | Operation + Budget | Contratos e fixtures comuns |
| Contratos | Novo namespace Operation.V1, preservar Inventory atual | OS + Inventory + Budget | Pacote/commit fixado e roundtrip |
| Bancos | Operation PostgreSQL; OS SQL Server; Budget Mongo; Inventory PostgreSQL | Infra + donos | Credenciais, migrations e deploy |
| Nome | Operation é o serviço chamado Execution nos diagramas antigos | Todos | Documentação e queues sem ambiguidade |
| Filiais | V1 demonstra uma filial lógica; definir segregação antes de expandir | Todos | Escopo explícito/novo contrato se necessário |

O fluxo detalhado e decisões estão em [Saga](../architecture/SAGA.md), [ADRs](../INDEX.md) e [Contratos](../contracts/README.md).

### Correções propostas para outros repositórios

- Budget: retirar estoque do Execution; distinguir ExecutionAuthorized/Started; aguardar StockConsumed para finalização; usar CancelOperation no protocolo de compensação; corrigir afirmação de compensação para todos os passos.
- Inventory: confirmar ADR-0003; revisar regra para confirmação/replay de reserva e consulta de preços; manter ReserveStock com itens únicos, não vazios.
- OS: implementar compensações confirmadas, estado de recuperação, timeouts duráveis por etapa e fluxos sem itens de estoque.
- Operation: evoluir estado inicial para autorização separada, snapshots, CancelOperation, contratos reais e persistência.
- Monólito: manter documentação histórica da fase 3 e criar guia de migração; sem remover runtime antes do corte validado.

## Alternatives

Coreografia continua possível, mas exigiria revisar documentação e código existente. Pagamento após reserva evita cobrar sem disponibilidade, porém retém estoque durante pagamento; decidir explicitamente se o grupo preferir essa ordem. Nesta baseline seguimos pagamento antes de reserva como já proposto pelo Budget.

## Risks & Open Questions

Confirmar regras comerciais de timeout/reembolso, tratamento de chegada tardia de pagamento, catálogo e autenticação da RFC-002, artefato de contratos da RFC-003. Não prometer que refazer deploy resolve falha permanente de contrato ou ausência de reserva após reparo; RecoveryRequired precisa de ação operacional documentada.

## Decision

Pendente de revisão. Registrar responsáveis, data, alternativas aceitas, versão do contrato e PRs afetados. Até lá, todos os contratos novos são propostos.
