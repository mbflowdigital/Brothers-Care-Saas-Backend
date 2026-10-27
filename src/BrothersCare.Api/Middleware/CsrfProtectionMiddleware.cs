using BrothersCare.Api.Identity;
using Microsoft.AspNetCore.Antiforgery;

namespace BrothersCare.Api.Middleware;

public sealed class CsrfProtectionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context, IAntiforgery antiforgery, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(antiforgery);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        if (context.Request.Path.StartsWithSegments("/api") && !environment.IsDevelopment() && !context.Request.IsHttps)
        {
            await AuthenticationProblems.Create(context, StatusCodes.Status403Forbidden,
                "Esta operação exige uma conexão HTTPS.", "https-required").ExecuteAsync(context);
            return;
        }

        var method = context.Request.Method;
        if (!context.Request.Path.StartsWithSegments("/api") ||
            !(HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method)))
        {
            await next(context);
            return;
        }

        if (!AuthEndpoints.IsAllowedOrigin(context, configuration))
        {
            await AuthenticationProblems.Create(context, StatusCodes.Status403Forbidden,
                "Origem da solicitação não permitida.", "forbidden").ExecuteAsync(context);
            return;
        }

        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            await AuthenticationProblems.Create(context, StatusCodes.Status400BadRequest,
                "Token de proteção da solicitação ausente ou inválido.", "csrf-invalid").ExecuteAsync(context);
            return;
        }

        await next(context);
    }
}
