using System.Text.Json.Serialization;
using BrothersCare.Application.Identity.Contracts;
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
        endpoints.MapGet("/api/v1/team-members", ListAsync)
            .RequireAuthorization(IdentityPolicies.ClinicAdministrator)
            .WithTags("Equipe")
            .WithName("ListTenantTeam")
            .WithSummary("Lista a equipe da empresa ativa, com papéis, unidades e status.")
            .Produces<IReadOnlyList<TenantTeamMemberResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
        endpoints.MapPatch("/api/v1/team-members/{membershipId:guid}", UpdateAsync)
            .RequireAuthorization(IdentityPolicies.ClinicAdministrator)
            .WithTags("Equipe").WithName("UpdateTenantTeamMember")
            .WithSummary("Altera perfil e unidades de uma pessoa da equipe.")
            .Produces(StatusCodes.Status204NoContent);
        endpoints.MapPost("/api/v1/team-members/{membershipId:guid}/deactivation", DeactivateAsync)
            .RequireAuthorization(IdentityPolicies.ClinicAdministrator)
            .WithTags("Equipe").WithName("DeactivateTenantTeamMember")
            .WithSummary("Revoga o acesso preservando o histórico da pessoa.")
            .Produces(StatusCodes.Status204NoContent);
        return endpoints;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> UpdateAsync(
        Guid membershipId, [FromBody] UpdateTenantTeamMemberRequest? request,
        [FromServices] ManageTenantTeam manageTenantTeam, HttpContext context, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (request?.Role is null || request.ClinicUnitIds is null)
            return TeamProblem(context, TenantTeamMutationStatus.InvalidInput);
        var result = await manageTenantTeam.UpdateAsync(membershipId, request.Role, request.ClinicUnitIds,
            request.PerformsCare, request.Specialty, request.CouncilType, request.RegistrationNumber, cancellationToken);
        return result == TenantTeamMutationStatus.Success ? TypedResults.NoContent() : TeamProblem(context, result);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeactivateAsync(
        Guid membershipId, [FromServices] ManageTenantTeam manageTenantTeam,
        HttpContext context, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var result = await manageTenantTeam.DeactivateAsync(membershipId, cancellationToken);
        return result == TenantTeamMutationStatus.Success ? TypedResults.NoContent() : TeamProblem(context, result);
    }

    private static ProblemHttpResult TeamProblem(HttpContext context, TenantTeamMutationStatus status) => status switch
    {
        TenantTeamMutationStatus.Unauthenticated => AuthenticationProblems.Create(context, 401, "Sessão ausente, inválida ou expirada.", "authentication-required"),
        TenantTeamMutationStatus.Forbidden => AuthenticationProblems.Create(context, 403, "Você não tem permissão para administrar esta pessoa.", "forbidden"),
        TenantTeamMutationStatus.NotFound => AuthenticationProblems.Create(context, 404, "Pessoa não encontrada nesta equipe.", "not-found"),
        TenantTeamMutationStatus.UnitNotFound => AuthenticationProblems.Create(context, 404, "Uma das unidades selecionadas não está disponível.", "unit-not-found"),
        TenantTeamMutationStatus.CannotDeactivateSelf => AuthenticationProblems.Create(context, 409, "Para sua segurança, você não pode desativar o próprio acesso.", "cannot-deactivate-self"),
        TenantTeamMutationStatus.LastAdministrator => AuthenticationProblems.Create(context, 409, "Mantenha ao menos um administrador ativo na clínica.", "last-administrator"),
        TenantTeamMutationStatus.InvalidState => AuthenticationProblems.Create(context, 409, "O acesso atual não permite esta alteração.", "invalid-state"),
        _ => AuthenticationProblems.Create(context, 400, "Revise o perfil e as unidades selecionadas.", "validation"),
    };

    private static async Task<Results<Ok<IReadOnlyList<TenantTeamMemberResponse>>, ProblemHttpResult>> ListAsync(
        [FromServices] ListTenantTeam listTenantTeam,
        [FromServices] ICurrentPrincipalAccessor principalAccessor,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var result = await listTenantTeam.ExecuteAsync(cancellationToken);
        return result.Status switch
        {
            TenantTeamStatus.Success => TypedResults.Ok<IReadOnlyList<TenantTeamMemberResponse>>(result.Items.Select(item =>
                new TenantTeamMemberResponse(item.MembershipId, item.UserId, item.DisplayName, item.Email,
                    item.Role, item.Status, item.CreatedAt,
                    item.Units.Select(unit => new TenantTeamUnitResponse(unit.Id, unit.Name)).ToArray(),
                    item.UserId == principalAccessor.Principal?.UserId,
                    item.PerformsCare, item.Specialty, item.CouncilType, item.RegistrationNumber)).ToArray()),
            TenantTeamStatus.Forbidden => AuthenticationProblems.Create(context, StatusCodes.Status403Forbidden,
                "Você não tem permissão para consultar a equipe desta empresa.", "forbidden"),
            _ => AuthenticationProblems.Create(context, StatusCodes.Status401Unauthorized,
                "Sessão ausente, inválida ou expirada.", "authentication-required"),
        };
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
            request.Email, request.DisplayName, request.Role, request.ClinicUnitIds,
            request.PerformsCare, request.Specialty, request.CouncilType, request.RegistrationNumber), cancellationToken);
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
public sealed record InviteTenantUserHttpRequest(
    string? Email,
    string? DisplayName,
    string? Role,
    Guid[]? ClinicUnitIds,
    bool PerformsCare,
    string? Specialty,
    string? CouncilType,
    string? RegistrationNumber)
{
    public override string ToString() => "[REDACTED]";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateTenantTeamMemberRequest(
    string? Role,
    Guid[]? ClinicUnitIds,
    bool PerformsCare,
    string? Specialty,
    string? CouncilType,
    string? RegistrationNumber);

public sealed record TenantTeamMemberResponse(
    Guid MembershipId,
    Guid UserId,
    string DisplayName,
    string Email,
    string Role,
    string Status,
    DateTimeOffset CreatedAt,
    IReadOnlyList<TenantTeamUnitResponse> Units,
    bool IsCurrentUser,
    bool PerformsCare,
    string? Specialty,
    string? CouncilType,
    string? RegistrationNumber);

public sealed record TenantTeamUnitResponse(Guid Id, string Name);
