using System.Security.Claims;
using System.Text.Encodings.Web;
using BrothersCare.Application.Identity.Sessions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace BrothersCare.Api.Identity;

public sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    SessionCookie cookie) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "BrothersCareSession";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null &&
            Context.GetEndpoint()?.Metadata.GetMetadata<AuthenticateOptionalSession>() is null)
        {
            return AuthenticateResult.NoResult();
        }

        var value = cookie.Read(Context);
        if (value is null)
        {
            return AuthenticateResult.NoResult();
        }

        // Persistence is optional for the public health endpoints. Resolve it only when a cookie is supplied.
        var getSession = Context.RequestServices.GetService<GetCurrentSession>();
        var session = getSession is null ? null : await getSession.AuthenticateAsync(value, Context.RequestAborted);
        if (session is null)
        {
            cookie.Clear(Context);
            return AuthenticateResult.Fail("Invalid session.");
        }

        Context.Features.Set(session);
        var principal = session.Principal;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, principal.UserId.ToString("D")),
            new("session_id", principal.SessionId.ToString("D")),
        };
        if (principal.PlatformRole is { } platformRole)
        {
            claims.Add(new Claim("platform_role", platformRole));
        }

        if (principal.ActiveTenantId is { } tenantId && principal.TenantMembershipId is { } membershipId)
        {
            claims.Add(new Claim("active_tenant_id", tenantId.ToString("D")));
            claims.Add(new Claim("tenant_membership_id", membershipId.ToString("D")));
            if (principal.TenantRole is { } tenantRole)
            {
                claims.Add(new Claim("tenant_role", tenantRole));
            }
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        cookie.Clear(Context);
        return AuthenticationProblems.Create(Context, StatusCodes.Status401Unauthorized,
            "Sessão ausente, inválida ou expirada.", "authentication-required").ExecuteAsync(Context);
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        AuthenticationProblems.Create(Context, StatusCodes.Status403Forbidden,
            "Você não tem permissão para realizar esta operação.", "forbidden").ExecuteAsync(Context);
}
