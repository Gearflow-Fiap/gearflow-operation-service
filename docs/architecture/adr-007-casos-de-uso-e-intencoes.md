# ADR-007: Intenções idempotentes e casos de uso locais

**Status:** Accepted localmente para o passo 3, solicitado pelo usuário. **Date:** 2026-10-28. **Escopo:** Application e testes. Não é aprovação coletiva dos contratos nem prova de transação real.

## Context

A aplicação inicial alterava agregado carregado por referência e só emitia evento genérico. Faltavam preço por fonte confiável, consultas, replay, conflitos de versão e prova local de falha de commit sem vazamento de mutação.

## Decision

Usar CommandContext com intenção/OS/ator e versão esperada opcional. Gerar fingerprint canônico, buscar receipt antes de precondição/efeitos e executar mudanças em snapshot destacado. Entregar estado/receipt/outbox em um único contrato de Commit com CAS. Resultado vencedor de intenção concorrente é retornado; nova intenção sem mudança não duplica evento de negócio. Storage real deverá garantir atomicidade/constraints.

Obter preços por IPriceCatalog com DiagnosticPricer configurado EXPLICITAMENTE como LegacyLinePriceV1 ou PerUnitV1. Sem default de produção e sem preços livres de navegador. Preservar a política no snapshot; acordo com Budget ainda pendente.

Projetar DTOs de saída, paginação/filtros por read port e durações por fase. Publicação/HTTP/autenticação e adapter de banco ficam para incrementos próprios. OperationEvent agora é registro interno tipado por enum e com projeção de dados, não o pacote GearFlow.Contracts.

## Consequences

Repetições e falhas são verificáveis sem depender do OS pronto. Storage passou a ter responsabilidades explícitas e mais exigentes. IsolatedStore somente nos testes não substitui banco real; é necessário comprovar CAS/atomicidade/inbox/outbox com PostgreSQL e broker antes de declarar confiabilidade distribuída.

Resultados de replay são históricos; cliente deve consultar estado atual. Confirmação de comando repetido para a Saga é trabalho do adapter futuro, usando receipt/outbox sem repetir efeito de negócio. Formato do fingerprint e retenção de recibos/tombstones precisam de migração segura em versões futuras.

## Alternatives Considered

- Salvar e publicar depois: perde entrega em queda entre etapas.
- Alterar referência compartilhada: vaza estado mesmo quando o commit falha.
- Refazer precificação a cada retry: altera valor já fechado e aumenta acoplamento.
- Escolher fórmula de insumos implicitamente: diverge do legado/Budget sem acordo.
- Carregar todas as OS na aplicação para paginar: inadequado ao escopo de escala do desafio; adapter deve paginar/agregar na fonte.

## Related

- [Casos de uso do passo 3](../planning/STEP_3_APPLICATION.md).
- [ADR-006](adr-006-modelo-local-e-precificacao.md).
- [Critérios de qualidade](../planning/STEP_ACCEPTANCE.md).
