using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;

namespace BrothersCare.Api.Identity;

internal static class AuthenticationProblems
{
    public static ProblemHttpResult RateLimited(HttpContext context, int seconds)
    {
        context.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        return Create(context, StatusCodes.Status429TooManyRequests,
            "Limite de tentativas atingido. Aguarde para tentar novamente.", "rate-limit");
    }

    public static ProblemHttpResult Create(HttpContext context, int status, string title, string problem)
    {
        context.Response.Headers.CacheControl = "no-store";
        return TypedResults.Problem(statusCode: status, title: title,
            type: $"https://api.brotherscare.com/problems/{problem}",
            extensions: new Dictionary<string, object?>
            {
                ["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier,
            });
    }
}
