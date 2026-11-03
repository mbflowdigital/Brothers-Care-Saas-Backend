using System.Text.Json.Serialization;
using BrothersCare.Api.Identity;
using BrothersCare.Application.Tenancy;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace BrothersCare.Api.Tenancy;

public static class ClinicUnitEndpoints
{
    public static IEndpointRouteBuilder MapClinicUnitEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/clinic-units", ListAsync)
            .RequireAuthorization(IdentityPolicies.ClinicAdministrator)
            .WithTags("Unidades")
            .WithName("ListClinicUnits")
            .WithSummary("Lista as unidades da empresa ativa.")
            .Produces<ClinicUnitPage>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        endpoints.MapGet("/api/v1/clinic-units/{clinicUnitId:guid}", GetAsync)
            .RequireAuthorization(IdentityPolicies.ClinicAdministrator)
            .WithTags("Unidades")
            .WithName("GetClinicUnit")
            .WithSummary("Consulta uma unidade da empresa ativa.")
            .Produces<ClinicUnitDetails>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        endpoints.MapPost("/api/v1/clinic-units", CreateAsync)
            .RequireAuthorization(IdentityPolicies.ClinicAdministrator)
            .WithTags("Unidades")
            .WithName("CreateClinicUnit")
            .WithSummary("Cria uma unidade na empresa ativa.")
            .Produces<ClinicUnitItem>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        endpoints.MapPatch("/api/v1/clinic-units/{clinicUnitId:guid}", RenameAsync)
            .RequireAuthorization(IdentityPolicies.ClinicAdministrator)
            .WithTags("Unidades")
            .WithName("RenameClinicUnit")
            .WithSummary("Altera o nome de uma unidade da empresa ativa.")
            .Produces<ClinicUnitItem>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        endpoints.MapPost("/api/v1/clinic-units/{clinicUnitId:guid}/deactivation", DeactivateAsync)
            .RequireAuthorization(IdentityPolicies.ClinicAdministrator)
            .WithTags("Unidades")
            .WithName("DeactivateClinicUnit")
            .WithSummary("Desativa uma unidade e cancela seus agendamentos futuros.")
            .WithDescription("A operação preserva o histórico e não remaneja agendamentos para outra unidade.")
            .Produces<ClinicUnitDeactivationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<Results<Ok<ClinicUnitPage>, ProblemHttpResult>> ListAsync(
        [FromQuery] string? search,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromServices] ManageClinicUnits manageClinicUnits,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var result = await manageClinicUnits.ListAsync(search, page ?? 1, pageSize ?? 25, cancellationToken);
        return result.Status == ClinicUnitOperationStatus.Success && result.Page is not null
            ? TypedResults.Ok(result.Page)
            : Problem(context, result.Status);
    }

    private static async Task<Results<Ok<ClinicUnitDetails>, ProblemHttpResult>> GetAsync(
        Guid clinicUnitId,
        [FromServices] ManageClinicUnits manageClinicUnits,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var result = await manageClinicUnits.GetAsync(clinicUnitId, cancellationToken);
        return result.Status == ClinicUnitOperationStatus.Success && result.Unit is not null
            ? TypedResults.Ok(result.Unit)
            : Problem(context, result.Status);
    }

    private static async Task<Results<Created<ClinicUnitItem>, ProblemHttpResult>> CreateAsync(
        [FromBody] ClinicUnitRequest? request,
        [FromServices] ManageClinicUnits manageClinicUnits,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var result = await manageClinicUnits.CreateAsync(request?.Name ?? string.Empty, cancellationToken);
        return result.Status == ClinicUnitOperationStatus.Success && result.Unit is not null
            ? TypedResults.Created($"/api/v1/clinic-units/{result.Unit.Id:D}", result.Unit)
            : Problem(context, result.Status);
    }

    private static async Task<Results<Ok<ClinicUnitItem>, ProblemHttpResult>> RenameAsync(
        Guid clinicUnitId,
        [FromBody] ClinicUnitRequest? request,
        [FromServices] ManageClinicUnits manageClinicUnits,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var result = await manageClinicUnits.RenameAsync(clinicUnitId, request?.Name ?? string.Empty, cancellationToken);
        return result.Status == ClinicUnitOperationStatus.Success && result.Unit is not null
            ? TypedResults.Ok(result.Unit)
            : Problem(context, result.Status);
    }

    private static async Task<Results<Ok<ClinicUnitDeactivationResponse>, ProblemHttpResult>> DeactivateAsync(
        Guid clinicUnitId,
        [FromServices] ManageClinicUnits manageClinicUnits,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var result = await manageClinicUnits.DeactivateAsync(clinicUnitId, cancellationToken);
        return result.Status == ClinicUnitOperationStatus.Success && result.Unit is not null
            ? TypedResults.Ok(new ClinicUnitDeactivationResponse(
                result.Unit.Id,
                result.Unit.Name,
                result.Unit.IsActive,
                result.CancelledAppointments))
            : Problem(context, result.Status);
    }

    private static ProblemHttpResult Problem(HttpContext context, ClinicUnitOperationStatus status) =>
        status switch
        {
            ClinicUnitOperationStatus.Unauthenticated => AuthenticationProblems.Create(context, StatusCodes.Status401Unauthorized,
                "Sessão ausente, inválida ou expirada.", "authentication-required"),
            ClinicUnitOperationStatus.Forbidden => AuthenticationProblems.Create(context, StatusCodes.Status403Forbidden,
                "Você não tem permissão para administrar unidades nesta empresa.", "forbidden"),
            ClinicUnitOperationStatus.NotFound => AuthenticationProblems.Create(context, StatusCodes.Status404NotFound,
                "Unidade não encontrada.", "resource-not-found"),
            ClinicUnitOperationStatus.NameAlreadyExists => AuthenticationProblems.Create(context, StatusCodes.Status409Conflict,
                "Já existe uma unidade com este nome nesta empresa.", "duplicate-clinic-unit-name"),
            ClinicUnitOperationStatus.AlreadyInactive => AuthenticationProblems.Create(context, StatusCodes.Status409Conflict,
                "A unidade já está desativada.", "invalid-state-transition"),
            _ => AuthenticationProblems.Create(context, StatusCodes.Status400BadRequest,
                "Revise os dados informados para a unidade.", "validation"),
        };
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ClinicUnitRequest(string? Name);

public sealed record ClinicUnitDeactivationResponse(Guid Id, string Name, bool IsActive, int CancelledAppointments);
