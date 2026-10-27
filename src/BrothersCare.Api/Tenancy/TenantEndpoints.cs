using System.Text.Json.Serialization;
using BrothersCare.Api.Identity;
using BrothersCare.Application.Tenancy;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace BrothersCare.Api.Tenancy;

public static class TenantEndpoints
{
    public static IEndpointRouteBuilder MapTenantEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapPost("/api/v1/tenants", CreateAsync)
            .RequireAuthorization(IdentityPolicies.SuperAdministrator)
            .WithTags("Empresas")
            .WithName("CreateTenantWithAdministrator")
            .WithSummary("Cria uma empresa e convida seu primeiro administrador.")
            .WithDescription("Exige superadministrador da plataforma. Não concede acesso clínico ao operador.")
            .Produces<CreatedTenantResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return endpoints;
    }

    private static async Task<Results<Created<CreatedTenantResponse>, ProblemHttpResult>> CreateAsync(
        [FromBody] CreateTenantHttpRequest? request,
        [FromServices] CreateTenantWithAdministrator createTenant,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (request?.TenantName is null || request.AdministratorEmail is null || request.AdministratorDisplayName is null)
        {
            return AuthenticationProblems.Create(context, StatusCodes.Status400BadRequest,
                "Informe o nome da empresa, o nome e o e-mail do primeiro administrador.", "validation");
        }

        var result = await createTenant.ExecuteAsync(new CreateTenantWithAdministratorCommand(
            request.TenantName, request.AdministratorEmail, request.AdministratorDisplayName, request.ClinicUnitName), cancellationToken);
        if (result.Status == TenantProvisioningStatus.Created && result.TenantId is { } tenantId)
        {
            return TypedResults.Created((string?)null, new CreatedTenantResponse(tenantId, result.ClinicUnitId));
        }

        return result.Status switch
        {
            TenantProvisioningStatus.Unauthenticated => AuthenticationProblems.Create(context, StatusCodes.Status401Unauthorized,
                "Sessão ausente, inválida ou expirada.", "authentication-required"),
            TenantProvisioningStatus.Forbidden => AuthenticationProblems.Create(context, StatusCodes.Status403Forbidden,
                "Você não tem permissão para criar uma empresa.", "forbidden"),
            TenantProvisioningStatus.InvalidInput => AuthenticationProblems.Create(context, StatusCodes.Status400BadRequest,
                "Revise os dados da empresa e do primeiro administrador.", "validation"),
            _ => AuthenticationProblems.Create(context, StatusCodes.Status503ServiceUnavailable,
                "Não foi possível concluir o cadastro da empresa. Tente novamente.", "service-unavailable"),
        };
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateTenantHttpRequest(
    string? TenantName, string? AdministratorEmail, string? AdministratorDisplayName, string? ClinicUnitName = null)
{
    public override string ToString() => "[REDACTED]";
}

public sealed record CreatedTenantResponse(Guid TenantId, Guid? ClinicUnitId);
