using System.Security.Claims;

namespace GearFlow.Operation.Api;

/// <summary>O ator vem exclusivamente do sub autenticado; não exige perfil Staff nem GUID.</summary>
public static class AuthenticatedActor
{
    public static bool HasValidSubject(ClaimsPrincipal user)
    {
        var subjects = user.FindAll("sub").ToArray();
        return subjects.Length == 1 && !string.IsNullOrWhiteSpace(subjects[0].Value) && subjects[0].Value.Length <= 194;
    }

    public static string Read(ClaimsPrincipal user)
    {
        if (!user.Identities.Any(identity => identity.IsAuthenticated) || !HasValidSubject(user))
            throw new HttpInputException(401, "invalid_actor");
        // Preserva o texto original: ator participa do fingerprint de idempotência e do histórico.
        // Prefixo histórico preservado para manter fingerprints e replays existentes.
        return "staff:" + user.FindFirstValue("sub");
    }
}
