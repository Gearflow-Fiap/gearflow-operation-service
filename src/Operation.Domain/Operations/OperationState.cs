namespace GearFlow.Operation.Domain;

// Formato de persistência interno versionado, distinto dos contratos HTTP/mensageria.
internal sealed record StoredDiagnostic(Guid DiagnosticId, DiagnosticLine[] Lines, DateTimeOffset PricedAt, string PricingPolicyId);
internal sealed record StoredAuthorization(Guid AuthorizationId, Guid BudgetId, int BudgetRevision, ApprovedJob[] Jobs);
internal sealed record OperationState(int FormatVersion, Guid ServiceOrderId, int? ServiceOrderCode, Guid? InitialVehicleId,
    Guid? DiagnosticId, RequestedJob[] RequestedJobs, RequestedPart[] RequestedParts,
    DiagnosticConsumable[] Consumables, StoredDiagnostic? Diagnostic, StoredAuthorization? Authorization);
