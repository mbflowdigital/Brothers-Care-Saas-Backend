using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Application.Identity.Sessions;

namespace BrothersCare.Api.Identity;

public sealed class CurrentPrincipalAccessor(IHttpContextAccessor httpContextAccessor) : ICurrentPrincipalAccessor
{
    public CurrentPrincipal? Principal =>
        httpContextAccessor.HttpContext?.Features.Get<AuthenticatedSession>()?.Principal;
}
