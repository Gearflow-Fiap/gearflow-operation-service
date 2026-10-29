using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using W = GearFlow.Contracts.Operation.V1;

var root = Path.GetFullPath(args.Length == 0 ? "docs/contracts/runtime-v1" : args[0]);
Directory.CreateDirectory(Path.Combine(root, "examples")); Directory.CreateDirectory(Path.Combine(root, "schemas"));
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(), WriteIndented = true, NumberHandling = JsonNumberHandling.Strict, RespectRequiredConstructorParameters = true };
var queues = new Dictionary<string, string>
{
    ["StartDiagnostic"] = "operation-start-diagnostic", ["StartExecution"] = "operation-start-execution", ["CancelOperation"] = "operation-cancel-operation",
    ["UpdateOperationVehicle"] = "operation-update-vehicle", ["DeactivateOperation"] = "operation-deactivate"
};
var types = typeof(W.StartDiagnostic).Assembly.GetTypes().Where(type => type.Namespace == typeof(W.StartDiagnostic).Namespace && type.GetProperty("MessageId") is not null).OrderBy(type => type.Name).ToArray();
foreach (var type in types)
{
    var schema = options.GetJsonSchemaAsNode(type, new JsonSchemaExporterOptions
    {
        TreatNullObliviousAsNonNullable = true,
        TransformSchemaNode = (context, node) =>
        {
            if (node is not JsonObject obj) return node;
            var name = context.PropertyInfo?.Name ?? "";
            if (context.TypeInfo.Type == typeof(Guid)) obj["not"] = new JsonObject { ["const"] = Guid.Empty.ToString() };
            if (context.TypeInfo.Type == typeof(DateTimeOffset)) obj["pattern"] = "(Z|\\+00:00)$";
            if (context.TypeInfo.Type == typeof(string)) { obj["minLength"] = 1; obj["maxLength"] = 2000; }
            if (context.TypeInfo.Type.IsArray) { obj["maxItems"] = 200; if (name is "requestedJobs" or "items") obj["minItems"] = 1; }
            if (name.EndsWith("PriceCents", StringComparison.OrdinalIgnoreCase) || name.EndsWith("TotalCents", StringComparison.OrdinalIgnoreCase)) { obj["minimum"] = 0; obj["maximum"] = int.MaxValue; }
            if (name == "serviceOrderCode") obj["minimum"] = 1;
            if (name == "operationVersion") obj["minimum"] = 1;
            if (name == "budgetRevision") obj["const"] = 1;
            if (name == "currency") obj["const"] = "BRL";
            if (name == "quantity") { obj["exclusiveMinimum"] = 0; obj["multipleOf"] = 0.001m; }
            return node;
        }
    });
    schema["$schema"] = "https://json-schema.org/draft/2020-12/schema"; schema["title"] = type.FullName;
    File.WriteAllText(Path.Combine(root, "schemas", type.Name + ".schema.json"), schema.ToJsonString(options) + Environment.NewLine);
    File.WriteAllText(Path.Combine(root, "examples", type.Name + ".json"), JsonSerializer.Serialize(Example(type), type, options) + Environment.NewLine);
}
var manifest = new { status = "implemented-local-preview-not-published", package = "GearFlow.Contracts.Operation", version = "1.0.0-preview.1",
    messages = types.Select(type => new { name = type.Name, kind = queues.ContainsKey(type.Name) ? "command" : "event", @namespace = type.Namespace,
        urn = "urn:message:" + type.Namespace + ":" + type.Name, destinationQueue = queues.GetValueOrDefault(type.Name),
        schema = "schemas/" + type.Name + ".schema.json", example = "examples/" + type.Name + ".json" }) };
File.WriteAllText(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(manifest, options) + Environment.NewLine);
Console.WriteLine($"{types.Length} contratos exportados para {root}");

static Guid Id(int value) => Guid.Parse($"10000000-0000-4000-8000-{value:000000000000}");
static object? Example(Type type)
{
    var constructor = type.GetConstructors().OrderByDescending(item => item.GetParameters().Length).First();
    return constructor.Invoke(constructor.GetParameters().Select(parameter => Value(type, parameter)).ToArray());
}
static object? Value(Type owner, ParameterInfo parameter)
{
    var type = parameter.ParameterType; var name = parameter.Name!;
    if (Nullable.GetUnderlyingType(type) == typeof(long)) return null;
    if (Nullable.GetUnderlyingType(type) == typeof(int)) return 45;
    if (type == typeof(Guid)) return name switch
    {
        "CorrelationId" => Id(1), "MessageId" => Id(2), "CausationId" => Id(3), "DiagnosticId" => Id(4), "BudgetId" => Id(5),
        "AuthorizationMessageId" => Id(6), "VehicleId" => Id(7), "LineId" when owner == typeof(W.RequestedPart) => Id(10),
        "LineId" or "DiagnosticLineId" => Id(8), "CatalogJobId" or "ItemId" => Id(9), "PartId" => Id(11), "BudgetJobId" => Id(12), _ => throw new InvalidOperationException(name)
    };
    if (type == typeof(DateTimeOffset)) return new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
    if (type == typeof(string)) return name switch { "Currency" => "BRL", "PricingPolicyId" => "per-unit-away-from-zero-v1", "ActorId" => "system:os", "CommandName" => "StartExecution", "Code" => "StateConflict", "Name" => "Mão de obra", _ => "Exemplo de auditoria" };
    if (type == typeof(long)) return 1L;
    if (type == typeof(int)) return name is "BudgetRevision" or "Quantity" ? 1 : 1000;
    if (type == typeof(decimal)) return 1m;
    if (type == typeof(W.CommercialItemType)) return W.CommercialItemType.Labor;
    if (type == typeof(Guid[])) return new[] { Id(12) };
    if (type.IsArray) { var array = Array.CreateInstance(type.GetElementType()!, 1); array.SetValue(Example(type.GetElementType()!), 0); return array; }
    return Example(type);
}


