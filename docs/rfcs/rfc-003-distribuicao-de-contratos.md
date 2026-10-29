# RFC-003: Distribuição e evolução de GearFlow.Contracts

**Status:** Draft. **Date:** 2026-10-24. **Reviewers:** mantenedores de todos os serviços.

## Summary

Definir um artefato de contratos com release identificável e responsabilidade coletiva. Inventory já contém src/GearFlow.Contracts; Budget propõe pacote NuGet compartilhado, mas não foi verificada publicação. Os schemas Operation aqui são contrato proposto, não release utilizável em produção.

## Proposal

1. Durante revisão, usar docs/contracts como fonte da proposta Operation e o código Inventory como referência dos tipos já implementados. Não manter dois masters permanentes.
2. Escolher repositório/owner do artefato comum: preferir projeto de contratos com release independente em repositório dedicado, se o grupo aceitar criar esse repositório; alternativa transitória é publicar o projeto existente do Inventory. Nenhum repositório novo foi criado.
3. Publicar GearFlow.Contracts com versão fixa e changelog; serviço referencia pacote, nunca caminho ../gearflow-inventory-service.
4. Preservar namespace GearFlow.Contracts para ReserveStock/StockReserved etc. Novos tipos Operation usam GearFlow.Contracts.Operation.V1, assembly acordado GearFlow.Contracts. Nome de tipo/namespace faz parte do URN do MassTransit; a versão do pacote sozinha não substitui o tipo wire.
5. Contratos sem EF, entidades, MediatR ou dependência do host. Target framework mínimo compatível com consumidores reais (.NET 8 é apenas proposta do Budget; validar antes de fixar).
6. Testes publicador/consumidor verificam URN, nomes/case dos campos, enum string, GUID, timestamps UTC, decimais, campos extras e exemplos. Gerar exemplos pelo serializador real e comparar ao contrato antes do primeiro release.
7. SemVer: adições opcionais compatíveis em minor; remoção, obrigatoriedade nova, enum novo sem fallback ou mudança semântica exige revisão major e convivência dos tipos/consumidores. Um novo campo BranchId com mudança de autorização exige revisão explícita.

## Roteamento

Comandos Operation por Send: operation-start-diagnostic, operation-start-execution e operation-cancel-operation. Eventos por Publish: Saga se inscreve nos tipos necessários. Nomes de filas dos consumidores da Saga são responsabilidade OS; não derivar compatibilidade apenas do nome da fila.

Inventory preserva inventory-reserve-stock, inventory-release-stock, inventory-consume-stock e tipos atuais. A convenção Publish de comando também funciona hoje no Inventory, mas a baseline prefere Send direcionado sem quebrar bindings existentes.

## Alternatives

Duplicar records por copiar/colar: drift e incompatibilidade de tipo. Gerar tipos a partir do schema: possível, mas exige configurar identidade de namespace e testes MassTransit. Compartilhar biblioteca de domínio: acopla implementações e não serve como artefato de integração.

## Risks & Open Questions

Falta definir repositório canônico, mantenedor/revisores, registry, acesso de CI e primeira versão. Não afirmar que basta copiar JSON numa fila para falar com MassTransit: envelope, content-type e identidade de tipo precisam ser compatíveis.

## Atualização local — incremento 6

Operation implementou pacote separado GearFlow.Contracts.Operation 1.0.0-preview.1, namespace GearFlow.Contracts.Operation.V1 e target .NET 10. Preserva identidade URN e evita copiar/renomear Inventory. Nupkg local inclui schemas/exemplos gerados do código; sem publicação em registry. Esta alternativa transitória substitui a hipótese de assembly única nesta implementação local, sem presumir decisão coletiva sobre repo/owner/registry. Ver docs/contracts/README.md e ADR-011.

## Decision

Pendente. Contratos podem ser usados para desenvolver simuladores e validar fixtures; publicação/código gerado virão em incremento separado.
