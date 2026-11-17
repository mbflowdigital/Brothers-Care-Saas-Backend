using System.Text.Json.Serialization;
using BrothersCare.Api.Identity;
using BrothersCare.Application.Care;
using BrothersCare.Application.Care.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace BrothersCare.Api.Care;

public static class ClinicalRecordEndpoints
{
    public static IEndpointRouteBuilder MapClinicalRecordEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1").RequireAuthorization().WithTags("Evoluções clínicas");
        group.MapGet("/clinical-records/options", OptionsAsync).WithName("GetClinicalRecordOptions");
        group.MapGet("/patients/{patientId:guid}/clinical-records", ListAsync).WithName("ListClinicalRecords");
        group.MapGet("/clinical-records/{recordId:guid}", GetAsync).WithName("GetClinicalRecord");
        group.MapPost("/clinical-records", CreateAsync).WithName("CreateClinicalRecord");
        group.MapPatch("/clinical-records/{recordId:guid}", UpdateAsync).WithName("UpdateClinicalRecord");
        group.MapPost("/clinical-records/{recordId:guid}/finalization", FinalizeAsync)
            .WithName("FinalizeClinicalRecord");
        return endpoints;
    }

    private static async Task<Results<Ok<ClinicalRecordOptions>, ProblemHttpResult>> OptionsAsync(
        [FromServices] ManageClinicalRecords service, HttpContext context, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var result = await service.GetOptionsAsync(cancellationToken);
        return result.Status == ClinicalRecordOperationStatus.Success && result.Options is not null
            ? TypedResults.Ok(result.Options)
            : Problem(context, result.Status);
    }

    private static async Task<Results<Ok<IReadOnlyList<ClinicalRecordSummary>>, ProblemHttpResult>> ListAsync(
        Guid patientId, [FromServices] ManageClinicalRecords service, HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var result = await service.ListAsync(patientId, cancellationToken);
        return result.Status == ClinicalRecordOperationStatus.Success && result.Items is not null
            ? TypedResults.Ok(result.Items)
            : Problem(context, result.Status);
    }

    private static async Task<Results<Ok<ClinicalRecordDetails>, ProblemHttpResult>> GetAsync(
        Guid recordId, [FromServices] ManageClinicalRecords service, HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var result = await service.GetAsync(recordId, cancellationToken);
        return result.Status == ClinicalRecordOperationStatus.Success && result.Record is not null
            ? TypedResults.Ok(result.Record)
            : Problem(context, result.Status);
    }

    private static async Task<Results<Created<ClinicalRecordDetails>, ProblemHttpResult>> CreateAsync(
        [FromBody] ClinicalRecordCreateRequest? request, [FromServices] ManageClinicalRecords service,
        HttpContext context, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var result = await service.CreateAsync(new ClinicalRecordInput(request?.PatientId ?? Guid.Empty,
            request?.ClinicUnitId ?? Guid.Empty, request?.PatientReport, request?.ProceduresAndObservations,
            request?.AssessmentAndPlan), cancellationToken);
        return result.Status == ClinicalRecordOperationStatus.Success && result.Record is not null
            ? TypedResults.Created($"/api/v1/clinical-records/{result.Record.Id:D}", result.Record)
            : Problem(context, result.Status);
    }

    private static async Task<Results<Ok<ClinicalRecordDetails>, ProblemHttpResult>> UpdateAsync(
        Guid recordId, [FromBody] ClinicalRecordUpdateRequest? request,
        [FromServices] ManageClinicalRecords service, HttpContext context, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var result = await service.UpdateAsync(recordId, new ClinicalRecordUpdateInput(
            request?.PatientReport, request?.ProceduresAndObservations, request?.AssessmentAndPlan,
            request?.Version ?? string.Empty), cancellationToken);
        return result.Status == ClinicalRecordOperationStatus.Success && result.Record is not null
            ? TypedResults.Ok(result.Record)
            : Problem(context, result.Status);
    }

    private static async Task<Results<Ok<ClinicalRecordDetails>, ProblemHttpResult>> FinalizeAsync(
        Guid recordId, [FromBody] ClinicalRecordFinalizationRequest? request,
        [FromServices] ManageClinicalRecords service, HttpContext context, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var result = await service.FinalizeAsync(recordId,
            new ClinicalRecordFinalizationInput(request?.Version ?? string.Empty), cancellationToken);
        return result.Status == ClinicalRecordOperationStatus.Success && result.Record is not null
            ? TypedResults.Ok(result.Record)
            : Problem(context, result.Status);
    }

    private static ProblemHttpResult Problem(HttpContext context, ClinicalRecordOperationStatus status) => status switch
    {
        ClinicalRecordOperationStatus.Unauthenticated => AuthenticationProblems.Create(context, 401,
            "Sessão ausente, inválida ou expirada.", "authentication-required"),
        ClinicalRecordOperationStatus.Forbidden => AuthenticationProblems.Create(context, 403,
            "Você não possui vínculo assistencial para acessar este prontuário.", "clinical-access-denied"),
        ClinicalRecordOperationStatus.NotFound => AuthenticationProblems.Create(context, 404,
            "Evolução clínica não encontrada.", "clinical-record-not-found"),
        ClinicalRecordOperationStatus.Finalized => AuthenticationProblems.Create(context, 409,
            "Uma evolução finalizada não pode ser alterada.", "clinical-record-finalized"),
        ClinicalRecordOperationStatus.FinalizationInvalid => AuthenticationProblems.Create(context, 422,
            "Informe os procedimentos e a avaliação com as próximas condutas antes de finalizar.",
            "clinical-record-incomplete"),
        ClinicalRecordOperationStatus.Conflict => AuthenticationProblems.Create(context, 409,
            "Esta evolução foi atualizada em outra sessão. Recarregue antes de continuar.",
            "clinical-record-conflict"),
        _ => AuthenticationProblems.Create(context, 400,
            "Confira os dados informados para a evolução clínica.", "validation"),
    };
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ClinicalRecordCreateRequest(
    Guid? PatientId,
    Guid? ClinicUnitId,
    string? PatientReport,
    string? ProceduresAndObservations,
    string? AssessmentAndPlan);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ClinicalRecordUpdateRequest(
    string? PatientReport,
    string? ProceduresAndObservations,
    string? AssessmentAndPlan,
    string? Version);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ClinicalRecordFinalizationRequest(string? Version);
