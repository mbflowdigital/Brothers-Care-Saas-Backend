using BrothersCare.Application.Identity;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace BrothersCare.Api.Identity;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/auth").WithTags("Authentication");

        group.MapPost("/activation", ActivateAsync)
            .WithName("ActivateAccount")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return endpoints;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> ActivateAsync(
        [FromBody] AccountActivationHttpRequest? request,
        [FromServices] IAccountActivationService activationService,
        CancellationToken cancellationToken)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.Token) ||
            string.IsNullOrEmpty(request.Password) ||
            string.IsNullOrEmpty(request.PasswordConfirmation))
        {
            return InvalidActivationRequest();
        }

        var result = await activationService.ActivateAsync(
            new AccountActivationRequest(request.Token, request.Password, request.PasswordConfirmation),
            cancellationToken);

        return result is AccountActivationResult.Activated
            ? TypedResults.NoContent()
            : InvalidActivationRequest();
    }

    private static ProblemHttpResult InvalidActivationRequest() => TypedResults.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Unable to activate the account.",
        type: "https://api.brotherscare.com/problems/account-activation-invalid");
}

public sealed record AccountActivationHttpRequest(
    string? Token,
    string? Password,
    string? PasswordConfirmation);
