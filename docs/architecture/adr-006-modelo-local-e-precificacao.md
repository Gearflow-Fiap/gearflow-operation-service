# ADR-006: Modelo local e preços autoritativos no passo 2

**Status:** Accepted localmente para o modelo do passo 2, por autorização do usuário para executar o plano. **Date:** 2026-10-28. **Escopo:** Operation.Domain. Não representa aceite dos outros responsáveis nem publicação de contratos.

## Context

A matriz do passo 1 mostrou ausência de veículo/peças/consumíveis no núcleo e confusão entre ID do catálogo e linha executada. Também há uma diferença entre a soma de insumos do monólito e o cálculo por unidade proposto nos contratos.

## Decision

Implementar dados e regras operacionais no agregado, com snapshot imutável e mapeamento BudgetJobId/DiagnosticLineId/CatalogJobId. Adotar estados técnicos separados de estado global, autorização distinta do início físico, histórico versionado e atividade/cancelamento. Campos/enum são internos, sem identidade wire publicada.

Preservar UnitPriceCents e LineTotalCents fornecidos por uma política identificada, rejeitar negativos e overflow ao somar; não impor fórmula nova de insumos antes de alinhamento financeiro. Application deverá obter preço de fonte confiável e escolher a política explicitamente no próximo passo. Não aceitar snapshot diretamente do navegador.

## Consequences

Dados de fase 3 não são perdidos e o domínio é testável sem serviços externos. API de domínio anterior foi substituída e as assinaturas do esqueleto Application foram adaptadas para compilar, sem declarar os casos de uso completos. Contratos JSON anteriores ainda precisam incluir dados iniciais, identidades e política compatíveis, após revisão coletiva.

O snapshot não verifica que a fonte foi autenticada nem valida uma fórmula financeira universal; esse limite é intencional e deve ser resolvido na aplicação/integração. Persistência e idempotência de mensagens continuam posteriores.

## Alternatives Considered

- Preservar construtor antigo sem veículo/itens: permitiria continuar criando operação incompleta.
- Tratar CatalogJobId como BudgetJobId: perderia progresso independente de linhas repetidas.
- Recalcular insumo automaticamente por unidade: mudaria regra financeira do legado sem acordo.
- Manter referências mutáveis recebidas do chamador: permitiria alterar diagnóstico/aprovação sem ação auditada.

## Related

- [Matriz de paridade](../planning/STEP_1_PARITY.md).
- [Implementação do modelo](../planning/STEP_2_MODEL.md).
- [ADR-004](adr-004-execucao-e-compensacao.md) (proposta distribuída, não aprovada coletivamente por este ADR).
