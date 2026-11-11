namespace BrothersCare.Application.Identity.Contracts;

public interface ICurrentPrincipalAccessor
{
    CurrentPrincipal? Principal { get; }
}

public sealed record CurrentPrincipal(
    Guid UserId,
    Guid SessionId,
    Guid? ActiveTenantId,
    string? PlatformRole,
    Guid? TenantMembershipId,
    string? TenantRole,
    IReadOnlyList<Guid> ClinicUnitIds)
{
    public override string ToString() => "[REDACTED]";
}
