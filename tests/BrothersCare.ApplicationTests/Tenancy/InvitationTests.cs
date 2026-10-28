using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Application.Identity.Invitations;
using BrothersCare.Application.Tenancy;
using BrothersCare.ApplicationTests.Support;
using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.ApplicationTests.Tenancy;

public sealed class InvitationTests
{
    [Fact]
    public async Task InvitationReusesGlobalUserWithoutChangingOtherTenantOrPassword()
    {
        var scenario = AdminScenario();
        var target = scenario.AddUser(email: "target@example.invalid");
        var (_, externalMembership, _) = scenario.AddTenant(target);
        var originalHash = Assert.Single(scenario.Store.Credentials, item => item.UserId == target.Id).PasswordHash;
        var unitId = scenario.Store.Units[0].Id;
        Assert.Equal(InvitationStatus.Accepted, await Invite(scenario).ExecuteAsync(Command(target.Email, unitId), CancellationToken.None));

        Assert.Equal(2, scenario.Store.Users.Count);
        Assert.Equal(2, scenario.Store.Credentials.Count);
        Assert.Equal(originalHash, Assert.Single(scenario.Store.Credentials, item => item.UserId == target.Id).PasswordHash);
        Assert.Equal(MembershipStatus.Active, externalMembership.Status);
        var invited = Assert.Single(scenario.Store.Memberships, item => item.UserId == target.Id && item.TenantId == scenario.Principal!.ActiveTenantId);
        Assert.Equal(MembershipStatus.Invited, invited.Status);
        Assert.Equal(TenantMembershipRole.Professional, invited.Role);
        Assert.Contains(scenario.Store.UnitMemberships, item => item.TenantMembershipId == invited.Id && item.ClinicUnitId == unitId);
        var token = Assert.Single(scenario.Store.Tokens);
        Assert.Equal(invited.Id, token.TenantMembershipId);
        Assert.Equal(AccountActionTokenPurpose.InvitationAcceptance, token.Purpose);
        Assert.True(scenario.Tokens.Matches(Assert.Single(scenario.Invitations).Token, token.SecretHash));
        Assert.Contains(AuthenticationAuditEvent.InvitationIssued, scenario.Events);
    }

    [Fact]
    public async Task NewUserReceivesPendingCredentialAndInvitedMembership()
    {
        var scenario = AdminScenario();
        Assert.Equal(InvitationStatus.Accepted, await Invite(scenario).ExecuteAsync(Command(" NEW@EXAMPLE.INVALID ", scenario.Store.Units[0].Id), CancellationToken.None));
        var user = Assert.Single(scenario.Store.Users, item => item.NormalizedEmail == "NEW@EXAMPLE.INVALID");
        var credential = Assert.Single(scenario.Store.Credentials, item => item.UserId == user.Id);
        Assert.Equal(UserCredentialStatus.PendingActivation, credential.Status);
        Assert.Null(credential.PasswordHash);
    }

    [Fact]
    public async Task ReissueReusesMembershipAndRevokesOnlyItsPreviousTokens()
    {
        var scenario = AdminScenario();
        var user = scenario.AddUser(email: "target@example.invalid");
        var (_, external, _) = scenario.AddTenant(user);
        external.Deactivate();
        external.RenewInvitation(external.Role);
        scenario.AddToken(user, AccountActionTokenPurpose.InvitationAcceptance, external.Id);
        var invitation = Command(user.Email, scenario.Store.Units[0].Id);
        var service = Invite(scenario);
        Assert.Equal(InvitationStatus.Accepted, await service.ExecuteAsync(invitation, CancellationToken.None));
        var member = Assert.Single(scenario.Store.Memberships, item => item.UserId == user.Id && item.Id != external.Id);
        Assert.Equal(InvitationStatus.Accepted, await service.ExecuteAsync(invitation, CancellationToken.None));
        Assert.Equal(3, scenario.Store.Memberships.Count);
        Assert.Single(scenario.Store.Tokens, item => item.TenantMembershipId == member.Id && item.RevokedAt is not null);
        Assert.Single(scenario.Store.Tokens, item => item.TenantMembershipId == member.Id && item.RevokedAt is null);
        Assert.Null(Assert.Single(scenario.Store.Tokens, item => item.TenantMembershipId == external.Id).RevokedAt);
        Assert.Contains(AuthenticationAuditEvent.InvitationReissued, scenario.Events);
    }

    [Fact]
    public async Task RevokedMemberRequiresNewInvitationAndOnlyItsTenantSessionsAreRevoked()
    {
        var scenario = AdminScenario();
        var actor = scenario.Principal;
        var user = scenario.AddUser(email: "target@example.invalid");
        var tenant = scenario.Store.Tenants[0];
        var member = new TenantMembership(Guid.NewGuid(), tenant.Id, user.Id, TenantMembershipRole.Professional, scenario.Clock.GetUtcNow());
        scenario.Store.Memberships.Add(member);
        scenario.SignIn(user, member);
        var (_, external, _) = scenario.AddTenant(user);
        scenario.SignIn(user, external);
        member.Deactivate();
        scenario.Principal = actor;

        Assert.Equal(InvitationStatus.Accepted, await Invite(scenario).ExecuteAsync(Command(user.Email, scenario.Store.Units[0].Id), CancellationToken.None));
        Assert.Equal(MembershipStatus.Invited, member.Status);
        Assert.NotNull(Assert.Single(scenario.Store.Sessions, item => item.UserId == user.Id && item.ActiveTenantId == tenant.Id).RevokedAt);
        Assert.Null(Assert.Single(scenario.Store.Sessions, item => item.UserId == user.Id && item.ActiveTenantId == external.TenantId).RevokedAt);
    }

    [Theory]
    [InlineData("foreign-unit", InvitationStatus.UnitNotFound)]
    [InlineData("unassigned-unit", InvitationStatus.Forbidden)]
    [InlineData("inactive-unit", InvitationStatus.UnitNotFound)]
    [InlineData("professional", InvitationStatus.Forbidden)]
    [InlineData("revoked-actor", InvitationStatus.Forbidden)]
    [InlineData("suspended-tenant", InvitationStatus.Forbidden)]
    [InlineData("changed-session", InvitationStatus.ContextChanged)]
    [InlineData("expired-session", InvitationStatus.Unauthenticated)]
    public async Task InvitationRevalidatesAuthorizationFromStore(string invalidity, InvitationStatus expected)
    {
        var scenario = AdminScenario();
        var unit = scenario.Store.Units[0];
        var member = Assert.Single(scenario.Store.Memberships);
        switch (invalidity)
        {
            case "foreign-unit":
                var other = scenario.AddUser(email: "other@example.invalid");
                unit = scenario.AddTenant(other).Unit;
                break;
            case "unassigned-unit":
                unit = new ClinicUnit(Guid.NewGuid(), member.TenantId, "Unidade sem permissão", scenario.Clock.GetUtcNow());
                scenario.Store.Units.Add(unit);
                break;
            case "inactive-unit": unit.Deactivate(); break;
            case "professional": member.ChangeRole(TenantMembershipRole.Professional); break;
            case "revoked-actor": member.Deactivate(); break;
            case "suspended-tenant": scenario.Store.Tenants[0].Suspend(); break;
            case "changed-session":
                var user = scenario.Store.Users[0];
                var otherMember = scenario.AddTenant(user).Membership;
                scenario.Store.Sessions[0].SelectActiveTenant(otherMember, scenario.Clock.GetUtcNow());
                break;
            case "expired-session": scenario.Clock.Advance(TimeSpan.FromMinutes(30)); break;
        }
        Assert.Equal(expected, await Invite(scenario).ExecuteAsync(Command("new@example.invalid", unit.Id), CancellationToken.None));
        Assert.Empty(scenario.Store.Tokens);
        Assert.Empty(scenario.Invitations);
        Assert.Equal(0, scenario.Store.Commits);
    }

    [Fact]
    public async Task ActiveMembershipCannotBeDowngradedToInvitation()
    {
        var scenario = AdminScenario();
        var user = scenario.Store.Users[0];
        Assert.Equal(InvitationStatus.MembershipConflict, await Invite(scenario).ExecuteAsync(Command(user.Email, scenario.Store.Units[0].Id), CancellationToken.None));
        Assert.Equal(MembershipStatus.Active, Assert.Single(scenario.Store.Memberships).Status);
        Assert.Empty(scenario.Store.Tokens);
    }

    [Fact]
    public async Task SuspendedMembershipCannotBeReinvited()
    {
        var scenario = AdminScenario();
        var user = scenario.AddUser(email: "target@example.invalid");
        var member = new TenantMembership(Guid.NewGuid(), scenario.Principal!.ActiveTenantId!.Value,
            user.Id, TenantMembershipRole.Professional, scenario.Clock.GetUtcNow());
        scenario.Store.Memberships.Add(member);
        IdentityScenario.LoadSuspendedMembership(member);
        Assert.Equal(InvitationStatus.MembershipConflict, await Invite(scenario).ExecuteAsync(Command(user.Email, scenario.Store.Units[0].Id), CancellationToken.None));
        Assert.Equal(MembershipStatus.Suspended, member.Status);
        Assert.Empty(scenario.Store.Tokens);
    }

    [Theory]
    [InlineData("SuperAdministrator")]
    [InlineData("1")]
    [InlineData("professional")]
    [InlineData("")]
    public async Task InvitationRejectsUnknownOrPlatformRole(string role)
    {
        var scenario = AdminScenario();
        var command = Command("new@example.invalid", scenario.Store.Units[0].Id) with { Role = role };
        Assert.Equal(InvitationStatus.InvalidInput, await Invite(scenario).ExecuteAsync(command, CancellationToken.None));
        Assert.Empty(scenario.Invitations);
    }

    [Fact]
    public async Task MissingDeliveryOrFullPlanDoesNotCreateUserOrInvitation()
    {
        var scenario = AdminScenario();
        var command = Command("new@example.invalid", scenario.Store.Units[0].Id);
        Assert.Equal(InvitationStatus.PlanLimitExceeded, await Invite(scenario, maximumMemberships: 1).ExecuteAsync(command, CancellationToken.None));
        Assert.Equal(InvitationStatus.DeliveryUnavailable, await Invite(scenario, senderAvailable: false).ExecuteAsync(command, CancellationToken.None));
        Assert.Single(scenario.Store.Users);
        Assert.Single(scenario.Store.Memberships);
        Assert.Empty(scenario.Store.Tokens);
        Assert.Equal(0, scenario.Store.Commits);
    }

    private static IdentityScenario AdminScenario()
    {
        var scenario = new IdentityScenario();
        var user = scenario.AddUser();
        var (_, membership, _) = scenario.AddTenant(user);
        scenario.SignIn(user, membership);
        return scenario;
    }

    private static InviteTenantUser Invite(IdentityScenario scenario, int maximumMemberships = 25, bool senderAvailable = true) =>
        new(scenario, scenario.Store, new ProvisionalTenantInvitationPlanPolicy(scenario.Store,
            new ProvisionalInvitationPlanSettings(maximumMemberships)), scenario.Tokens, senderAvailable ? scenario : null,
            scenario.Clock, scenario);

    private static InviteTenantUserCommand Command(string email, Guid unitId) =>
        new(email, "Pessoa convidada", "Professional", [unitId]);
}
