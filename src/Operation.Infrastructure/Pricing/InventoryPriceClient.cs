using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using GearFlow.Operation.Application;
using GearFlow.Operation.Domain;

namespace GearFlow.Operation.Infrastructure.Pricing;

public interface IInventoryCredential { string? GetBearerToken(); }

/// <summary>Consulta somente contratos GET existentes; não acessa banco Inventory nem encaminha JWT do usuário.</summary>
public sealed class InventoryPriceClient(HttpClient http, IInventoryCredential credential)
{
    public async Task<CatalogPrice> Get(PriceRequest item, CancellationToken ct)
    {
        var path = item.ItemType switch { OperationItemType.Part => "parts", OperationItemType.Consumable => "consumables", _ => throw new ArgumentException("Tipo Inventory inválido.") };
        if (item.ItemId == Guid.Empty) throw new ArgumentException("Item obrigatório.");
        var token = credential.GetBearerToken();
        if (string.IsNullOrWhiteSpace(token) || http.BaseAddress is null) throw new PricingUnavailableException("Acesso Inventory não configurado.");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"api/inventory/{path}/{item.ItemId:D}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode == HttpStatusCode.NotFound) throw new InvalidPriceQuoteException("Item Inventory ausente.");
            if (!response.IsSuccessStatusCode) throw new PricingUnavailableException("Inventory não disponibilizou a cotação.");
            await response.Content.LoadIntoBufferAsync(65536, ct);
            using var json = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(ct));
            var root = json.RootElement;
            var id = root.GetProperty("id").GetGuid();
            var name = root.GetProperty("name").GetString();
            var price = root.GetProperty(item.ItemType == OperationItemType.Part ? "priceCents" : "unitPriceCents").GetInt32();
            if (id != item.ItemId || string.IsNullOrWhiteSpace(name) || name.Length > 2000 || price < 0 ||
                root.TryGetProperty("isActive", out var active) && !active.GetBoolean())
                throw new InvalidPriceQuoteException("Item Inventory inválido/inativo.");
            return new(item.ItemType, id, name, price);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new PricingUnavailableException("Timeout Inventory."); }
        catch (HttpRequestException) { throw new PricingUnavailableException("Inventory indisponível."); }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        { throw new InvalidPriceQuoteException("Resposta de preço Inventory inválida."); }
    }
}

public sealed class CompositePriceCatalog(IJobCatalog jobs, InventoryPriceClient inventory, TimeProvider clock) : IPriceCatalog
{
    public async Task<CatalogPriceQuote> GetPrices(IReadOnlyList<PriceRequest> items, CancellationToken ct)
    {
        if (items.Count > DiagnosticSnapshot.MaximumLines || items.Any(item => item.ItemId == Guid.Empty || !Enum.IsDefined(item.ItemType)))
            throw new ArgumentException("Consulta de preços inválida.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var wanted = items.Distinct().ToArray();
            var prices = new List<CatalogPrice>(await jobs.ReadPrices(wanted.Where(item => item.ItemType == OperationItemType.Labor).Select(item => item.ItemId).ToArray(), deadline.Token));
            // Uma consulta em voo por cotação, com prazo total finito; não cria centenas de requests concorrentes.
            foreach (var item in wanted.Where(item => item.ItemType != OperationItemType.Labor)) prices.Add(await inventory.Get(item, deadline.Token));
            return new(clock.GetUtcNow(), "BRL", prices.AsReadOnly());
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new PricingUnavailableException("Prazo total da cotação excedido."); }
    }
}
