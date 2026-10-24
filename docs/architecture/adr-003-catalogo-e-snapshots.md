# ADR-003: Catálogo de mão de obra e snapshots de preço

**Status:** Proposed. **Date:** 2026-10-24. **Decided by:** baseline local solicitada; revisão coletiva pendente. **Implementation:** parcial ou pendente conforme README.

## Context

Catalog existe no monólito; SqlPricingReader lê bancos/contextos diretamente. Budget precisa de preços confiáveis e Inventory é dono de peças/insumos.

## Decision

Propor catálogo de mão de obra no Operation. Peças e insumos permanecem no Inventory. Ao concluir diagnóstico, obter preços atuais por interfaces HTTP autorizadas (não SQL), congelar snapshot imutável e publicar DiagnosticFinished. Budget gera orçamento a partir do snapshot, não de valores livres do navegador. LineId acompanha a linha do diagnóstico até orçamento/progresso; ItemId referencia catálogo. V1 permite um diagnóstico e orçamento imutáveis por OS.

**Why:** Preservar valor apresentado ao cliente e rastrear exatamente o item aprovado sem criar um novo microsserviço de catálogo nesta fase.

## Consequences

### Positive

Preço auditável mesmo após alteração do catálogo.

### Negative

Conclusão do diagnóstico depende de disponibilidade de preços; mudanças de orçamento exigem novo desenho.

### Mitigations

Timeout, retorno 503 sem conclusão parcial, política explícita de arredondamento e contrato de preço em lote proposto com fallback para rotas atuais.

## Alternatives Considered

Catálogo independente: mais integração/deploy. Budget dono dos preços de catálogo: mistura formação de catálogo e cobrança. Preço enviado pelo cliente: falta autoridade. Leitura SQL de outro serviço: proibida.

## Related

- [Visão geral](OVERVIEW.md).
- [Saga](SAGA.md).
- [RFC de integração](../rfcs/rfc-001-baseline-e-integracao.md).
- [Contratos](../contracts/README.md).
