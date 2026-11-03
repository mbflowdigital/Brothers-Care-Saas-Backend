using System.Text.Json.Serialization;
using BrothersCare.Api.Identity;
using BrothersCare.Application.Tenancy;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace BrothersCare.Api.Tenancy;

public static class CommercialOnboardingEndpoints
{
    public static IEndpointRouteBuilder MapCommercialOnboardingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapPost("/api/v1/commercial-onboardings/simulated-payment", CreateAsync)
            .AllowAnonymous()
            .WithTags("Onboarding comercial")
            .WithName("CreateSimulatedCommercialOnboarding")
            .WithSummary("Simula a confirmação de compra e envia o convite inicial.")
            .WithDescription("Disponível somente em Development com configuração explícita. Não confirma pagamentos reais.")
            .Produces<CommercialOnboardingResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        endpoints.MapPost("/api/v1/commercial-onboardings/{tenantId:guid}/resend-simulated-invitation", ResendAsync)
            .AllowAnonymous()
            .WithTags("Onboarding comercial")
            .WithName("ResendSimulatedCommercialOnboardingInvitation")
            .WithSummary("Reenvia o convite de onboarding após uma compra simulada.")
            .WithDescription("Disponível somente em Development com configuração explícita. Revoga o token anterior antes de enviar outro.")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return endpoints;
    }

    private static async Task<Results<Created<CommercialOnboardingResponse>, ProblemHttpResult>> CreateAsync(
        [FromBody] CommercialOnboardingHttpRequest? request,
        [FromServices] CreateCommercialOnboarding createCommercialOnboarding,
        [FromServices] IHostEnvironment environment,
        [FromServices] IConfiguration configuration,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!environment.IsDevelopment() || !configuration.GetValue("CommercialOnboarding:AllowSimulatedPayment", false))
        {
            return AuthenticationProblems.Create(context, StatusCodes.Status404NotFound,
                "Recurso não encontrado.", "not-found");
        }

        if (request is null)
        {
            return AuthenticationProblems.Create(context, StatusCodes.Status400BadRequest,
                "Informe os dados da empresa e da pessoa responsável.", "validation");
        }

        var result = await createCommercialOnboarding.ExecuteAsync(new CreateCommercialOnboardingCommand(
            request.CompanyName ?? string.Empty,
            request.Cnpj ?? string.Empty,
            request.ResponsibleName ?? string.Empty,
            request.ResponsibleEmail ?? string.Empty), cancellationToken);
        if (result.Status == CommercialOnboardingStatus.Created && result.TenantId is { } tenantId)
        {
            return TypedResults.Created((string?)null, new CommercialOnboardingResponse(tenantId, "PendingOnboarding"));
        }

        return result.Status switch
        {
            CommercialOnboardingStatus.InvalidInput => AuthenticationProblems.Create(context, StatusCodes.Status400BadRequest,
                "Revise o nome da empresa, CNPJ, nome e e-mail da pessoa responsável.", "validation"),
            CommercialOnboardingStatus.CnpjAlreadyRegistered => AuthenticationProblems.Create(context, StatusCodes.Status409Conflict,
                "Já existe uma empresa cadastrada com este CNPJ.", "cnpj-already-registered"),
            _ => AuthenticationProblems.Create(context, StatusCodes.Status503ServiceUnavailable,
                "Não foi possível enviar o convite inicial. Tente novamente.", "service-unavailable"),
        };
    }

    private static async Task<Results<Accepted, ProblemHttpResult>> ResendAsync(
        Guid tenantId,
        [FromServices] ResendCommercialOnboardingInvitation resendInvitation,
        [FromServices] IHostEnvironment environment,
        [FromServices] IConfiguration configuration,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!environment.IsDevelopment() || !configuration.GetValue("CommercialOnboarding:AllowSimulatedPayment", false))
        {
            return AuthenticationProblems.Create(context, StatusCodes.Status404NotFound,
                "Recurso não encontrado.", "not-found");
        }

        var status = await resendInvitation.ExecuteAsync(tenantId, cancellationToken);
        return status switch
        {
            CommercialOnboardingResendStatus.Accepted => TypedResults.Accepted((string?)null),
            CommercialOnboardingResendStatus.NotFound => AuthenticationProblems.Create(context, StatusCodes.Status404NotFound,
                "Onboarding pendente não encontrado.", "not-found"),
            _ => AuthenticationProblems.Create(context, StatusCodes.Status503ServiceUnavailable,
                "Não foi possível reenviar o convite. Tente novamente.", "service-unavailable"),
        };
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CommercialOnboardingHttpRequest(
    string? CompanyName,
    string? Cnpj,
    string? ResponsibleName,
    string? ResponsibleEmail)
{
    public override string ToString() => "[REDACTED]";
}

public sealed record CommercialOnboardingResponse(Guid TenantId, string Status);
