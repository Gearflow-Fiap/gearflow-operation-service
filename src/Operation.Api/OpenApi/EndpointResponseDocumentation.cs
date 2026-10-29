using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;

namespace GearFlow.Operation.Api;

/// <summary>Only response metadata; authentication remains an explicit route policy.</summary>
public static class EndpointResponseDocumentation
{
    public static RouteGroupBuilder WithStaffErrorResponses(this RouteGroupBuilder group) => group.WithMetadata(
        Problem(400), Problem(401), Problem(403), Problem(500));

    public static RouteGroupBuilder WithOperationCommandResponses(this RouteGroupBuilder group) => group.WithMetadata(
        new ProducesResponseTypeMetadata(200, typeof(GearFlow.Operation.Application.CommandResult), ["application/json"]),
        Problem(404), Problem(409), Problem(412), Problem(428));

    private static ProducesResponseTypeMetadata Problem(int status) => new(status, typeof(ProblemDetails), ["application/problem+json"]);
}
