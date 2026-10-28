using System.Text.Json.Serialization;
using BrothersCare.Application.Identity.Invitations;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace BrothersCare.Api.Identity;

public static class InvitationEndpoints
{
    public static IEndpointRouteBuilder MapInvitationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapPost("/api/v1/invitations", InviteAsync)
            .RequireAuthorization(IdentityPolicies.ClinicAdministrator)
            .WithTags("Convites")
            .WithName("InviteTenantUser")
            .WithSummary("Convida uma pessoa para a empresa e unidades autorizadas da sessão.")
            .WithDescription("Exige administrador clínico, membership ativa e unidades permitidas. O tenant é derivado da sessão. Papéis: ClinicAdministrator, Professional ou Receptionist.")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return endpoints;
    }

    private static async Task<Results<Accepted, ProblemHttpResult>> InviteAsync(
        [FromBody] InviteTenantUserHttpRequest? request,
        [FromServices] InviteTenantUser inviteTenantUser,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (request?.Email is null || request.DisplayName is null || request.Role is null || request.ClinicUnitIds is null)
        {
            return AuthenticationProblems.Create(context, StatusCodes.Status400BadRequest,
                "Informe e-mail, nome, papel e unidades iniciais.", "validation");
        }

        var result = await inviteTenantUser.ExecuteAsync(new InviteTenantUserCommand(
            request.Email, request.DisplayName, request.Role, request.ClinicUnitIds), cancellationToken);
        if (result == InvitationStatus.Accepted)
        {
            return TypedResults.Accepted((string?)null);
        }

        return result switch
        {
            InvitationStatus.Unauthenticated => AuthenticationProblems.Create(context, StatusCodes.Status401Unauthorized,
                "Sessão ausente, inválida ou expirada.", "authentication-required"),
            InvitationStatus.Forbidden => AuthenticationProblems.Create(context, StatusCodes.Status403Forbidden,
                "Você não tem permissão para enviar este convite.", "forbidden"),
            InvitationStatus.InvalidInput => AuthenticationProblems.Create(context, StatusCodes.Status400BadRequest,
                "Revise o e-mail, nome, papel e unidades informados.", "validation"),
            InvitationStatus.UnitNotFound => AuthenticationProblems.Create(context, StatusCodes.Status404NotFound,
                "Unidade não encontrada ou indisponível para este convite.", "not-found"),
            InvitationStatus.MembershipConflict => AuthenticationProblems.Create(context, StatusCodes.Status409Conflict,
                "O vínculo existente não permite um novo convite neste estado.", "conflict"),
            InvitationStatus.ContextChanged => AuthenticationProblems.Create(context, StatusCodes.Status409Conflict,
                "A empresa da sessão foi alterada. Atualize a sessão antes de tentar novamente.", "conflict"),
            InvitationStatus.PlanLimitExceeded => AuthenticationProblems.Create(context, StatusCodes.Status422UnprocessableEntity,
                "O limite de usuários da empresa foi atingido.", "plan-limit"),
            _ => AuthenticationProblems.Create(context, StatusCodes.Status503ServiceUnavailable,
                "Não foi possível concluir o convite. Tente novamente.", "service-unavailable"),
        };
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InviteTenantUserHttpRequest(string? Email, string? DisplayName, string? Role, Guid[]? ClinicUnitIds)
{
    public override string ToString() => "[REDACTED]";
}
