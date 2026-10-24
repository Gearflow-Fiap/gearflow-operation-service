# Compatibilidade com Inventory implementado

Referência lida em 2026-10-24: src/GearFlow.Contracts/InventoryMessages.cs do gearflow-inventory-service. [Snapshot de leitura](reference/InventoryMessages.snapshot.cs); não compilar nem manter como fork do contrato. SHA-256 da referência: `38c195958f19164afd040ac1fd725175e30730dc3bc2f5fd17468880febfcf99`.

Tipos atuais ficam em GearFlow.Contracts (sem Operation.V1), com CorrelationId, MessageId e OccurredOn. ItemType é string Part/Consumable; Quantity é decimal. Não renomear campos nem URNs para adequar ao Operation. Implementação existente prevalece até mudança coordenada.

| Comando / evento | Campos específicos existentes | Dono |
|---|---|---|
| ReserveStock | Items: StockItemLine(ItemType, ItemId, Quantity) | Saga -> Inventory |
| ReleaseStock | Reason | Saga -> Inventory |
| ConsumeStock | sem campos extras | Saga -> Inventory |
| StockReserved | Lines: StockItemLine[] | Inventory -> Saga |
| StockInsufficient | Missing: MissingStockItem[] | Inventory -> Saga |
| StockReleased / StockConsumed | sem campos extras | Inventory -> Saga |
| StockReplenished | MessageId, OccurredOn, ItemType, ItemId; SEM CorrelationId | Inventory -> interessados |
| LowStockAlert | MessageId, OccurredOn, ItemType, ItemId, ItemName, Remaining, Minimum; SEM CorrelationId | Inventory -> interessados |

Filas: inventory-reserve-stock, inventory-release-stock, inventory-consume-stock. A Saga pode usar Send como descrito em QueueNames. Não adicionar CorrelationId de OS a eventos de reposição global nem exigir campos novos no payload atual.

Mapeamento do snapshot: ignorar Labor; para Part/Consumable, agrupar por ItemType/ItemId e somar Quantity. ReserveStock exige pelo menos um item e não aceita tipos/IDs repetidos. Se não houver itens de estoque, Saga pula reserva e consumo. Quantidades/IDs/preços do snapshot não significam disponibilidade garantida; só StockReserved confirma a reserva.

StockReplenished não autoriza iniciar reparo: Saga localiza OS em espera e reenvia ReserveStock. Retry de negócio após insuficiência usa novo MessageId; reentrega técnica da mesma tentativa mantém o ID. Saga guarda o último comando esperado e evita aceitar uma insuficiência antiga como falha da reserva já concluída. Contratos atuais Inventory não trazem CausationId/ReservationAttemptId: não há correlação de tentativa perfeita só pelo payload. Serializar tentativas por OS, tratar StockReserved como confirmação estável e ignorar StockInsufficient após reserva confirmada; se precisar distinguir tentativas, evoluir o contrato de forma coordenada. Não fingir que o novo OperationVersion resolve essa limitação externa.

Confirmar com Inventory o ADR-0003: ExecutionFinished leva a ConsumeStock e a OS só finaliza após StockConsumed; o ADR ainda está Proposed. Após consumo não liberar estoque para desfazer reparo físico. Testar tombstones e concorrência usando implementações existentes.

Preços: GET /api/inventory/parts/{id} devolve PriceCents; GET /api/inventory/consumables/{id} devolve UnitPriceCents. Endpoint em lote é apenas proposta na RFC-002. Autenticação de serviço ainda deve ser acordada.
