using System.Text.Json.Serialization;
using BrothersCare.Api.Identity;
using BrothersCare.Application.Care;
using BrothersCare.Application.Care.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace BrothersCare.Api.Care;

public static class PatientEndpoints
{
    public static IEndpointRouteBuilder MapPatientEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/patients", ListAsync)
            .RequireAuthorization()
            .WithTags("Pacientes")
            .WithName("ListPatients")
            .WithSummary("Lista os pacientes da empresa ativa.")
            .Produces<PatientPage>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        endpoints.MapGet("/api/v1/patients/registration-options", GetRegistrationOptionsAsync)
            .RequireAuthorization(IdentityPolicies.ClinicAdministrator)
            .WithTags("Pacientes")
            .WithName("GetPatientRegistrationOptions")
            .WithSummary("Lista profissionais e unidades válidos para vincular um novo paciente.")
            .Produces<PatientRegistrationOptions>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        endpoints.MapPost("/api/v1/patients", CreateAsync)
            .RequireAuthorization(IdentityPolicies.ClinicAdministrator)
            .WithTags("Pacientes")
            .WithName("CreatePatient")
            .WithSummary("Cadastra um paciente na empresa ativa.")
            .WithDescription("O tenant é derivado da sessão autenticada. O paciente é criado com status Active.")
            .Produces<PatientRegistrationItem>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        endpoints.MapPut("/api/v1/patients/{patientId:guid}", UpdateAsync)
            .RequireAuthorization(IdentityPolicies.ClinicAdministrator)
            .WithTags("Pacientes")
            .WithName("UpdatePatient")
            .WithSummary("Atualiza os dados e o vínculo assistencial de um paciente.")
            .Produces<PatientRegistrationItem>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        endpoints.MapDelete("/api/v1/patients/{patientId:guid}", DeactivateAsync)
            .RequireAuthorization(IdentityPolicies.ClinicAdministrator)
            .WithTags("Pacientes")
            .WithName("DeactivatePatient")
            .WithSummary("Desativa o paciente preservando seu histórico.")
            .Produces<PatientRegistrationItem>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<Results<Ok<PatientPage>, ProblemHttpResult>> ListAsync(
        [FromQuery] string? search,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        [FromServices] ListPatients listPatients,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var result = await listPatients.ExecuteAsync(
            search,
            page == 0 ? 1 : page,
            pageSize == 0 ? 10 : pageSize,
            cancellationToken);
        return result.Status == PatientRegistrationStatus.Success && result.Page is not null
            ? TypedResults.Ok(result.Page)
            : Problem(context, result.Status);
    }

    private static async Task<Results<Created<PatientRegistrationItem>, ProblemHttpResult>> CreateAsync(
        [FromBody] CreatePatientRequest? request,
        [FromServices] RegisterPatient registerPatient,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var result = await registerPatient.ExecuteAsync(
            request?.FullName ?? string.Empty,
            request?.BirthDate ?? default,
            request?.HealthPlan,
            request?.ProfessionalId,
            request?.ClinicUnitId,
            cancellationToken);

        return result.Status == PatientRegistrationStatus.Success && result.Patient is not null
            ? TypedResults.Created($"/api/v1/patients/{result.Patient.Id:D}", result.Patient)
            : Problem(context, result.Status);
    }

    private static async Task<Results<Ok<PatientRegistrationItem>, ProblemHttpResult>> UpdateAsync(
        Guid patientId,
        [FromBody] CreatePatientRequest? request,
        [FromServices] ManagePatient managePatient,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var result = await managePatient.UpdateAsync(patientId, request?.FullName ?? string.Empty,
            request?.BirthDate ?? default, request?.HealthPlan, request?.ProfessionalId,
            request?.ClinicUnitId, cancellationToken);
        return result.Status == PatientRegistrationStatus.Success && result.Patient is not null
            ? TypedResults.Ok(result.Patient)
            : Problem(context, result.Status);
    }

    private static async Task<Results<Ok<PatientRegistrationItem>, ProblemHttpResult>> DeactivateAsync(
        Guid patientId,
        [FromServices] ManagePatient managePatient,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var result = await managePatient.DeactivateAsync(patientId, cancellationToken);
        return result.Status == PatientRegistrationStatus.Success && result.Patient is not null
            ? TypedResults.Ok(result.Patient)
            : Problem(context, result.Status);
    }

    private static async Task<Results<Ok<PatientRegistrationOptions>, ProblemHttpResult>> GetRegistrationOptionsAsync(
        [FromServices] GetPatientRegistrationOptions getOptions,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var result = await getOptions.ExecuteAsync(cancellationToken);
        return result.Status == PatientRegistrationStatus.Success && result.Options is not null
            ? TypedResults.Ok(result.Options)
            : Problem(context, result.Status);
    }

    private static ProblemHttpResult Problem(HttpContext context, PatientRegistrationStatus status) =>
        status switch
        {
            PatientRegistrationStatus.Unauthenticated => AuthenticationProblems.Create(
                context,
                StatusCodes.Status401Unauthorized,
                "Sessão ausente, inválida ou expirada.",
                "authentication-required"),
            PatientRegistrationStatus.Forbidden => AuthenticationProblems.Create(
                context,
                StatusCodes.Status403Forbidden,
                "Você não tem permissão para cadastrar pacientes nesta empresa.",
                "forbidden"),
            PatientRegistrationStatus.AssignmentTargetNotFound => AuthenticationProblems.Create(
                context,
                StatusCodes.Status422UnprocessableEntity,
                "O profissional ou a unidade selecionada não está disponível para este vínculo.",
                "invalid-care-assignment"),
            PatientRegistrationStatus.NotFound => AuthenticationProblems.Create(
                context,
                StatusCodes.Status404NotFound,
                "Paciente não encontrado nesta empresa.",
                "patient-not-found"),
            _ => AuthenticationProblems.Create(
                context,
                StatusCodes.Status400BadRequest,
                "Informe o nome completo e a data de nascimento do paciente.",
                "validation"),
        };
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreatePatientRequest(
    string? FullName,
    DateOnly? BirthDate,
    string? HealthPlan,
    Guid? ProfessionalId,
    Guid? ClinicUnitId);
