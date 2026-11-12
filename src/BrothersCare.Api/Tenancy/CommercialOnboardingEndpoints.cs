using System.Text.Json.Serialization;
using BrothersCare.Api.Identity;
using BrothersCare.Application.Onboarding;
using BrothersCare.Application.Tenancy;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace BrothersCare.Api.Tenancy;

public static class CommercialOnboardingEndpoints
{
    public static IEndpointRouteBuilder MapCommercialOnboardingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapPost("/api/v1/commercial-onboardings/simulated-payment", CreateSimulatedAsync)
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
        endpoints.MapPost("/api/v1/commercial-onboardings/{tenantId:guid}/resend-simulated-invitation", ResendSimulatedAsync)
            .AllowAnonymous()
            .WithTags("Onboarding comercial")
            .WithName("ResendSimulatedCommercialOnboardingInvitation")
            .WithSummary("Reenvia o convite de onboarding após uma compra simulada.")
            .WithDescription("Disponível somente em Development com configuração explícita. Revoga o token anterior antes de enviar outro.")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        var preRegistrations = endpoints.MapGroup("/api/v1/tenant-pre-registrations")
            .RequireAuthorization(IdentityPolicies.SuperAdministrator)
            .WithTags("Pré-cadastros de empresas");
        preRegistrations.MapPost("", CreatePreRegistrationAsync).WithName("CreateTenantPreRegistration");
        preRegistrations.MapGet("/{tenantId:guid}", GetPreRegistrationAsync).WithName("GetTenantPreRegistration");
        preRegistrations.MapGet("/{tenantId:guid}/onboarding-link", GetOnboardingLinkAsync)
            .WithName("GetTenantPreRegistrationOnboardingLink")
            .Produces<CommercialOnboardingResendResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        preRegistrations.MapPut("/{tenantId:guid}", UpdatePreRegistrationAsync).WithName("UpdateTenantPreRegistration");
        preRegistrations.MapDelete("/{tenantId:guid}", CancelPreRegistrationAsync).WithName("CancelTenantPreRegistration");
        preRegistrations.MapDelete("/{tenantId:guid}/permanent", DeletePreRegistrationAsync)
            .WithName("DeleteTenantPreRegistration")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        preRegistrations.MapPost("/{tenantId:guid}/resend", ResendProtectedAsync)
            .WithName("ResendTenantPreRegistration")
            .Produces<CommercialOnboardingResendResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return endpoints;
    }

    private static Task<Results<Created<CommercialOnboardingResponse>, ProblemHttpResult>> CreatePreRegistrationAsync(
        [FromBody] CommercialOnboardingHttpRequest? request,
        [FromServices] CreateCommercialOnboarding createCommercialOnboarding,
        [FromServices] ManageTenantPreRegistration manager,
        [FromServices] IConfiguration configuration,
        HttpContext context,
        CancellationToken cancellationToken) => CreateCoreAsync(request, createCommercialOnboarding, manager, configuration, context, exposeOnboardingUrl: true, cancellationToken);

    private static async Task<Results<Created<CommercialOnboardingResponse>, ProblemHttpResult>> CreateSimulatedAsync(
        [FromBody] CommercialOnboardingHttpRequest? request,
        [FromServices] CreateCommercialOnboarding createCommercialOnboarding,
        [FromServices] ManageTenantPreRegistration manager,
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

        return await CreateCoreAsync(request, createCommercialOnboarding, manager, configuration, context,
            exposeOnboardingUrl: environment.IsDevelopment(), cancellationToken);
    }

    private static async Task<Results<Created<CommercialOnboardingResponse>, ProblemHttpResult>> CreateCoreAsync(
        CommercialOnboardingHttpRequest? request,
        CreateCommercialOnboarding createCommercialOnboarding,
        ManageTenantPreRegistration manager,
        IConfiguration configuration,
        HttpContext context,
        bool exposeOnboardingUrl,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
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
            var onboardingUrl = BuildOnboardingUrl(configuration, result.DevelopmentToken);
            if (onboardingUrl is not null)
            {
                await manager.SetOnboardingUrlAsync(tenantId, onboardingUrl, cancellationToken);
            }
            return TypedResults.Created((string?)null,
                new CommercialOnboardingResponse(tenantId, "PendingOnboarding", exposeOnboardingUrl ? onboardingUrl : null));
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

    private static async Task<Results<Ok<TenantPreRegistrationDetails>, ProblemHttpResult>> GetPreRegistrationAsync(
        Guid tenantId, [FromServices] ManageTenantPreRegistration manager, HttpContext context, CancellationToken cancellationToken)
    {
        var item = await manager.GetAsync(tenantId, cancellationToken);
        return item is null
            ? AuthenticationProblems.Create(context, StatusCodes.Status404NotFound, "Pré-cadastro não encontrado.", "not-found")
            : TypedResults.Ok(item);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> UpdatePreRegistrationAsync(
        Guid tenantId, [FromBody] CommercialOnboardingHttpRequest? request,
        [FromServices] ManageTenantPreRegistration manager, HttpContext context, CancellationToken cancellationToken)
    {
        if (request is null) return AuthenticationProblems.Create(context, 400, "Informe os dados do pré-cadastro.", "validation");
        var status = await manager.UpdateAsync(tenantId, new TenantPreRegistrationInput(
            request.CompanyName ?? string.Empty, request.Cnpj ?? string.Empty,
            request.ResponsibleName ?? string.Empty, request.ResponsibleEmail ?? string.Empty), cancellationToken);
        return status switch
        {
            TenantPreRegistrationStatus.Completed => TypedResults.NoContent(),
            TenantPreRegistrationStatus.NotFound => AuthenticationProblems.Create(context, 404, "Pré-cadastro não encontrado.", "not-found"),
            TenantPreRegistrationStatus.InvalidState => AuthenticationProblems.Create(context, 409, "Somente pré-cadastros pendentes podem ser editados.", "invalid-state"),
            TenantPreRegistrationStatus.CnpjAlreadyRegistered => AuthenticationProblems.Create(context, 409, "Já existe uma empresa cadastrada com este CNPJ.", "cnpj-already-registered"),
            _ => AuthenticationProblems.Create(context, 400, "Revise os dados do pré-cadastro.", "validation"),
        };
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> CancelPreRegistrationAsync(
        Guid tenantId, [FromServices] ManageTenantPreRegistration manager, HttpContext context, CancellationToken cancellationToken)
    {
        var status = await manager.CancelAsync(tenantId, cancellationToken);
        return status switch
        {
            TenantPreRegistrationStatus.Completed => TypedResults.NoContent(),
            TenantPreRegistrationStatus.NotFound => AuthenticationProblems.Create(context, 404, "Pré-cadastro não encontrado.", "not-found"),
            _ => AuthenticationProblems.Create(context, 409, "Somente pré-cadastros pendentes podem ser cancelados.", "invalid-state"),
        };
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeletePreRegistrationAsync(
        Guid tenantId,
        [FromServices] ManageTenantPreRegistration manager,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var status = await manager.DeleteAsync(tenantId, cancellationToken);
        return status switch
        {
            TenantPreRegistrationStatus.Completed => TypedResults.NoContent(),
            TenantPreRegistrationStatus.NotFound => AuthenticationProblems.Create(context, StatusCodes.Status404NotFound,
                "Pré-cadastro não encontrado.", "not-found"),
            _ => AuthenticationProblems.Create(context, StatusCodes.Status409Conflict,
                "Somente pré-cadastros que nunca foram ativados podem ser excluídos permanentemente.", "invalid-state"),
        };
    }

    private static async Task<Results<Accepted<CommercialOnboardingResendResponse>, ProblemHttpResult>> ResendProtectedAsync(
        Guid tenantId, [FromServices] ResendCommercialOnboardingInvitation resendInvitation,
        [FromServices] ManageTenantPreRegistration manager, [FromServices] IConfiguration configuration,
        HttpContext context, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var result = await resendInvitation.ExecuteAsync(tenantId, cancellationToken);
        if (result.Status == CommercialOnboardingResendStatus.Accepted)
        {
            var onboardingUrl = BuildOnboardingUrl(configuration, result.Token);
            if (onboardingUrl is null ||
                await manager.SetOnboardingUrlAsync(tenantId, onboardingUrl, cancellationToken) != TenantPreRegistrationStatus.Completed)
            {
                return AuthenticationProblems.Create(context, StatusCodes.Status503ServiceUnavailable,
                    "Não foi possível preparar o link de onboarding.", "service-unavailable");
            }
            return TypedResults.Accepted((string?)null, new CommercialOnboardingResendResponse(onboardingUrl));
        }
        return result.Status switch
        {
            CommercialOnboardingResendStatus.NotFound => AuthenticationProblems.Create(context, StatusCodes.Status404NotFound,
                "Onboarding pendente não encontrado.", "not-found"),
            _ => AuthenticationProblems.Create(context, StatusCodes.Status503ServiceUnavailable,
                "Não foi possível reenviar o convite. Tente novamente.", "service-unavailable"),
        };
    }

    private static async Task<Results<Ok<CommercialOnboardingResendResponse>, ProblemHttpResult>> GetOnboardingLinkAsync(
        Guid tenantId, [FromServices] ManageTenantPreRegistration manager,
        [FromServices] ResendCommercialOnboardingInvitation resendInvitation,
        [FromServices] GetOnboardingContext getOnboardingContext,
        [FromServices] IConfiguration configuration, HttpContext context, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var item = await manager.GetAsync(tenantId, cancellationToken);
        if (item is null)
        {
            return AuthenticationProblems.Create(context, StatusCodes.Status404NotFound,
                "Pré-cadastro não encontrado.", "not-found");
        }
        if (!string.Equals(item.Status, "PendingOnboarding", StringComparison.Ordinal))
        {
            return AuthenticationProblems.Create(context, StatusCodes.Status409Conflict,
                "O onboarding não está mais pendente.", "invalid-state");
        }
        if (TryGetToken(item.OnboardingUrl, out var storedToken) &&
            (await getOnboardingContext.ExecuteAsync(storedToken, item.Cnpj, cancellationToken)).Status == OnboardingContextStatus.Ready)
        {
            return TypedResults.Ok(new CommercialOnboardingResendResponse(item.OnboardingUrl));
        }

        var generated = await resendInvitation.ExecuteAsync(tenantId, cancellationToken);
        var onboardingUrl = generated.Status == CommercialOnboardingResendStatus.Accepted
            ? BuildOnboardingUrl(configuration, generated.Token)
            : null;
        if (onboardingUrl is null ||
            await manager.SetOnboardingUrlAsync(tenantId, onboardingUrl, cancellationToken) != TenantPreRegistrationStatus.Completed)
        {
            return AuthenticationProblems.Create(context, StatusCodes.Status503ServiceUnavailable,
                "Não foi possível preparar o link de onboarding.", "service-unavailable");
        }
        return TypedResults.Ok(new CommercialOnboardingResendResponse(onboardingUrl));
    }

    private static bool TryGetToken(string? onboardingUrl, out string? token)
    {
        token = null;
        if (!Uri.TryCreate(onboardingUrl, UriKind.Absolute, out var uri)) return false;
        token = QueryHelpers.ParseQuery(uri.Query)["token"].ToString();
        return !string.IsNullOrWhiteSpace(token);
    }

    private static async Task<Results<Accepted, ProblemHttpResult>> ResendSimulatedAsync(
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

        var result = await resendInvitation.ExecuteAsync(tenantId, cancellationToken);
        return result.Status switch
        {
            CommercialOnboardingResendStatus.Accepted => TypedResults.Accepted((string?)null),
            CommercialOnboardingResendStatus.NotFound => AuthenticationProblems.Create(context, StatusCodes.Status404NotFound,
                "Onboarding pendente não encontrado.", "not-found"),
            _ => AuthenticationProblems.Create(context, StatusCodes.Status503ServiceUnavailable,
                "Não foi possível reenviar o convite. Tente novamente.", "service-unavailable"),
        };
    }

    private static string? BuildOnboardingUrl(IConfiguration configuration, string? token)
    {
        var baseUrl = configuration["Email:Links:CommercialOnboardingUrl"];
        return token is not null && Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            ? QueryHelpers.AddQueryString(uri.ToString(), "token", token)
            : null;
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

public sealed record CommercialOnboardingResponse(Guid TenantId, string Status, string? OnboardingUrl = null)
{
    public override string ToString() => "[REDACTED]";
}

public sealed record CommercialOnboardingResendResponse(string? OnboardingUrl)
{
    public override string ToString() => "[REDACTED]";
}
