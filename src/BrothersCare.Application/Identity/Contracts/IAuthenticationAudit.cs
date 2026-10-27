namespace BrothersCare.Application.Identity.Contracts;

public interface IAuthenticationAudit
{
    void Record(AuthenticationAuditEvent action, DateTimeOffset occurredAt);
}

public enum AuthenticationAuditEvent
{
    LoginSucceeded = 1,
    Logout = 2,
    PasswordChanged = 3,
    AccountActivated = 4,
    AccountLocked = 5,
    PlatformAdministratorCreated = 6,
    TenantCreated = 7,
    InvitationIssued = 8,
    InvitationReissued = 9,
}
