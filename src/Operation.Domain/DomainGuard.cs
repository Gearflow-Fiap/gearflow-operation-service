namespace GearFlow.Operation.Domain;

internal static class DomainGuard
{
    public static Guid Id(Guid id, string name)
    {
        if (id == Guid.Empty) throw new ArgumentException("Identificador obrigatório.", name);
        return id;
    }

    public static string Text(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Texto obrigatório.", name);
        return value.Trim();
    }

    public static decimal Quantity(decimal quantity, string name)
    {
        if (quantity <= 0 || decimal.Round(quantity, 3) != quantity)
            throw new ArgumentOutOfRangeException(name, "Quantidade deve ser positiva, com até três casas decimais.");
        return quantity;
    }

    public static T[] CopyItems<T>(IEnumerable<T> items, string name) where T : class
    {
        ArgumentNullException.ThrowIfNull(items, name);
        var copy = items.ToArray();
        if (copy.Any(item => item is null)) throw new ArgumentException("Itens nulos não são permitidos.", name);
        return copy;
    }
}
