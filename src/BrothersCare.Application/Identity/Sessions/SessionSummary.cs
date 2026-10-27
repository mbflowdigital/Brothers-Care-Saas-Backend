namespace BrothersCare.Application.Identity.Sessions;

public sealed record SessionSummary(
    SessionUser User,
    SessionPlatformAdministrator? PlatformAdministrator,
    IReadOnlyList<SessionMembership> Memberships,
    SessionTenant? ActiveTenant,
    DateTimeOffset ExpiresAt);

public sealed record SessionUser(Guid Id, string DisplayName, string Email);
public sealed record SessionPlatformAdministrator(string Role);
public sealed record SessionMembership(Guid Id, Guid TenantId, string TenantName, string Role);
public sealed record SessionTenant(Guid Id, string Name);
