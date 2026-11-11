using System.Text.Json.Serialization;
using BrothersCare.Api.Identity;
using BrothersCare.Application.Onboarding;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace BrothersCare.Api.Onboarding;

public static class OnboardingEndpoints
{
    public static IEndpointRouteBuilder MapOnboardingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapPost("/api/v1/onboarding/context", GetContextAsync)
            .AllowAnonymous()
            .WithTags("Onboarding")
            .WithName("GetOnboardingContext")
            .WithSummary("Valida um convite comercial e retorna seu contexto mínimo.")
            .Produces<OnboardingContextResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status410Gone)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        endpoints.MapPost("/api/v1/onboarding/completion", CompleteAsync)
            .AllowAnonymous()
            .WithTags("Onboarding")
            .WithName("CompleteOnboarding")
            .WithSummary("Conclui o cadastro e ativa a primeira empresa e sua unidade padrão.")
            .Produces<OnboardingCompletionResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status410Gone);
        return endpoints;
    }

    private static async Task<Results<Ok<OnboardingContextResponse>, ProblemHttpResult>> GetContextAsync(
        [FromBody] OnboardingContextRequest? request,
        [FromServices] GetOnboardingContext getContext,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var result = await getContext.ExecuteAsync(request?.Token, request?.Cnpj, cancellationToken);
        if (result.Status == OnboardingContextStatus.Ready && result.Context is { } onboarding)
        {
            return TypedResults.Ok(new OnboardingContextResponse(
                onboarding.Email, onboarding.Cnpj, onboarding.CompanyName, onboarding.ExpiresAt));
        }

        return result.Status switch
        {
            OnboardingContextStatus.Expired => Problem(context, StatusCodes.Status410Gone,
                "Este convite expirou.", "expired-onboarding-token"),
            OnboardingContextStatus.Revoked => Problem(context, StatusCodes.Status410Gone,
                "Este convite foi revogado.", "revoked-onboarding-token"),
            OnboardingContextStatus.Used => Problem(context, StatusCodes.Status409Conflict,
                "Este convite já foi utilizado.", "used-onboarding-token"),
            OnboardingContextStatus.Unavailable => Problem(context, StatusCodes.Status503ServiceUnavailable,
                "O serviço de onboarding está temporariamente indisponível.", "onboarding-unavailable"),
            OnboardingContextStatus.CnpjMismatch => Problem(context, StatusCodes.Status400BadRequest,
                "O CNPJ não corresponde ao pré-cadastro deste convite.", "onboarding-cnpj-mismatch"),
            _ => Problem(context, StatusCodes.Status400BadRequest,
                "Este convite não é válido.", "invalid-onboarding-token"),
        };
    }

    private static ProblemHttpResult Problem(HttpContext context, int status, string title, string type) =>
        AuthenticationProblems.Create(context, status, title, type);

    private static async Task<Results<Ok<OnboardingCompletionResponse>, ProblemHttpResult>> CompleteAsync(
        [FromBody] OnboardingCompletionRequest? request,
        [FromServices] CompleteOnboarding completeOnboarding,
        [FromServices] AccountActionRateLimiter rateLimiter,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var denied = rateLimiter.Activation(context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        if (denied is { } seconds)
        {
            return AuthenticationProblems.RateLimited(context, seconds);
        }

        if (request?.Administrator is null || request.Company is null || request.ClinicUnit is null)
        {
            return Problem(context, StatusCodes.Status400BadRequest,
                "Revise os dados informados.", "validation");
        }

        var result = await completeOnboarding.ExecuteAsync(new CompleteOnboardingCommand(
            request.Token ?? string.Empty,
            request.Cnpj ?? string.Empty,
            new OnboardingAdministratorData(
                request.Administrator.DisplayName ?? string.Empty,
                request.Administrator.Cpf ?? string.Empty,
                request.Administrator.Password ?? string.Empty,
                request.Administrator.PasswordConfirmation ?? string.Empty),
            new OnboardingCompanyData(
                request.Company.LegalName ?? string.Empty,
                request.Company.TradeName ?? string.Empty,
                request.Company.CommercialEmail ?? string.Empty,
                request.Company.Phone ?? string.Empty),
            new OnboardingClinicUnitData(
                request.ClinicUnit.Name ?? string.Empty,
                request.ClinicUnit.PostalCode ?? string.Empty,
                request.ClinicUnit.Street ?? string.Empty,
                request.ClinicUnit.Number ?? string.Empty,
                request.ClinicUnit.Complement,
                request.ClinicUnit.District ?? string.Empty,
                request.ClinicUnit.City ?? string.Empty,
                request.ClinicUnit.State ?? string.Empty,
                request.ClinicUnit.Phone ?? string.Empty)), cancellationToken);

        return result.Status switch
        {
            OnboardingCompletionStatus.Completed => TypedResults.Ok(new OnboardingCompletionResponse("completed", "login")),
            OnboardingCompletionStatus.ExpiredToken => Problem(context, StatusCodes.Status410Gone,
                "Este convite expirou.", "expired-onboarding-token"),
            OnboardingCompletionStatus.RevokedToken => Problem(context, StatusCodes.Status410Gone,
                "Este convite foi revogado.", "revoked-onboarding-token"),
            OnboardingCompletionStatus.UsedToken => Problem(context, StatusCodes.Status409Conflict,
                "Este convite já foi utilizado.", "used-onboarding-token"),
            OnboardingCompletionStatus.CpfUnavailable => Problem(context, StatusCodes.Status409Conflict,
                "Não foi possível utilizar o CPF informado.", "cpf-unavailable"),
            OnboardingCompletionStatus.InvalidCredentials => Problem(context, StatusCodes.Status400BadRequest,
                "Não foi possível confirmar os dados de acesso.", "invalid-credentials"),
            OnboardingCompletionStatus.InvalidState => Problem(context, StatusCodes.Status409Conflict,
                "Este onboarding não pode ser concluído no estado atual.", "invalid-onboarding-state"),
            OnboardingCompletionStatus.InvalidToken => Problem(context, StatusCodes.Status400BadRequest,
                "Este convite não é válido.", "invalid-onboarding-token"),
            OnboardingCompletionStatus.CnpjMismatch => Problem(context, StatusCodes.Status400BadRequest,
                "O CNPJ não corresponde ao pré-cadastro deste convite.", "onboarding-cnpj-mismatch"),
            _ => Problem(context, StatusCodes.Status400BadRequest,
                "Revise os dados informados.", "validation"),
        };
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OnboardingContextRequest(string? Token, string? Cnpj)
{
    public override string ToString() => "[REDACTED]";
}

public sealed record OnboardingContextResponse(
    string Email,
    string Cnpj,
    string? CompanyName,
    DateTimeOffset ExpiresAt);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OnboardingCompletionRequest(
    string? Token,
    string? Cnpj,
    OnboardingAdministratorRequest? Administrator,
    OnboardingCompanyRequest? Company,
    OnboardingClinicUnitRequest? ClinicUnit)
{
    public override string ToString() => "[REDACTED]";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OnboardingAdministratorRequest(
    string? DisplayName,
    string? Cpf,
    string? Password,
    string? PasswordConfirmation)
{
    public override string ToString() => "[REDACTED]";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OnboardingCompanyRequest(
    string? LegalName,
    string? TradeName,
    string? CommercialEmail,
    string? Phone)
{
    public override string ToString() => "[REDACTED]";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OnboardingClinicUnitRequest(
    string? Name,
    string? PostalCode,
    string? Street,
    string? Number,
    string? Complement,
    string? District,
    string? City,
    string? State,
    string? Phone)
{
    public override string ToString() => "[REDACTED]";
}

public sealed record OnboardingCompletionResponse(string Status, string NextAction);
