using System.Globalization;

namespace GearFlow.Operation.Api;

/// <summary>Versão forte canônica, compartilhada por operações e catálogo.</summary>
public static class StrongETag
{
    public static long RequireIfMatch(HttpRequest request)
    {
        var header = request.Headers.IfMatch;
        if (header.Count == 0) throw new HttpInputException(428, "if_match_required");
        var text = header.Count == 1 ? header[0] : null;
        if (text is null || text.Length < 3 || text[0] != '"' || text[^1] != '"' ||
            !long.TryParse(text.AsSpan(1, text.Length - 2), NumberStyles.None, CultureInfo.InvariantCulture, out var version) ||
            version < 1 || Format(version) != text)
            throw new HttpInputException(400, "invalid_if_match");
        return version;
    }

    public static string Format(long version) => "\"" + version.ToString(CultureInfo.InvariantCulture) + "\"";

    public static void Write(HttpResponse response, long version)
    {
        response.Headers.ETag = Format(version);
        response.Headers.CacheControl = "no-store";
    }
}
