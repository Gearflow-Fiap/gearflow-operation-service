# ADR-005: Contratos tipados e evolução independente

**Status:** Proposed. **Date:** 2026-10-24. **Decided by:** baseline local solicitada; revisão coletiva pendente. **Implementation:** parcial ou pendente conforme README.

## Context

Inventory já possui GearFlow.Contracts com tipos e namespaces existentes. OperationEvent local é um envelope interno incompleto. Outros responsáveis precisam de contrato utilizável sem copiar entidades internas.

## Decision

Propor novos tipos Operation em GearFlow.Contracts.Operation.V1, distribuídos pelo mesmo artefato GearFlow.Contracts após acordo de ownership. Preservar integralmente tipos Inventory no namespace atual. Contratos descrevem corpo lógico e validações; usar envelope MassTransit com os mesmos MessageId/CorrelationId no corpo e no transporte. Eventos por Publish; comandos por Send à fila do destinatário. Schema e exemplos desta pasta são proposta documental, ainda sem pacote publicado.

**Why:** Compatibilidade exige identidade do tipo no broker, não apenas JSON parecido. Separar DTO de domínio evita acoplamento de persistência entre repositórios.

## Consequences

### Positive

Revisão e testes de contrato antes da integração.

### Negative

Versão compartilhada pode gerar coordenação de publicação; breaking changes exigem convivência.

### Mitigations

SemVer, versões fixas por consumidor, teste de serialização com MassTransit, mudanças aditivas opcionais; novo namespace major para ruptura e prazo de migração.

## Alternatives Considered

Copiar records entre repos: alto risco de drift e URN incompatível. Referenciar csproj de outro checkout: quebra builds independentes. Envelope genérico string Type: insuficiente como contrato de produção.

## Related

- [Visão geral](OVERVIEW.md).
- [Saga](SAGA.md).
- [RFC de integração](../rfcs/rfc-001-baseline-e-integracao.md).
- [Contratos](../contracts/README.md).
