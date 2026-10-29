using System.Security.Claims;

namespace GearFlow.Operation.Api;

/// <summary>A policy Staff valida a claim de perfil; o ator vem exclusivamente do sub autenticado.</summary>
public static class StaffActor
{
    public static bool HasValidSubject(ClaimsPrincipal user)
    {
        var subjects = user.FindAll("sub").ToArray();
        return subjects.Length == 1 && Guid.TryParse(subjects[0].Value, out var id) && id != Guid.Empty;
    }

    public static string Read(ClaimsPrincipal user)
    {
        if (!user.Identities.Any(identity => identity.IsAuthenticated) || !HasValidSubject(user))
            throw new HttpInputException(401, "invalid_actor");
        // Preserva o texto original: ator participa do fingerprint de idempotência e do histórico.
        return "staff:" + user.FindFirstValue("sub");
    }
}
