using System.Text.Json.Serialization;

namespace BrothersCare.Api.Identity;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LoginHttpRequest(string? Email, string? Password)
{
    public override string ToString() => "[REDACTED]";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SelectActiveTenantHttpRequest(Guid TenantId);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PasswordRecoveryRequestHttpRequest(string? Email)
{
    public override string ToString() => "[REDACTED]";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PasswordRecoveryConfirmationHttpRequest(string? Token, string? Password, string? PasswordConfirmation)
{
    public override string ToString() => "[REDACTED]";
}

public sealed record PasswordRecoveryRequestResponse(string Message);
