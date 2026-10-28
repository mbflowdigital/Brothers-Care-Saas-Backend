using BrothersCare.Application.Tenancy;
using BrothersCare.Application.Tenancy.Contracts;
using BrothersCare.ApplicationTests.Support;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.ApplicationTests.Tenancy;

public sealed class InvitationPlanTests
{
    [Fact]
    public async Task FullPlanRejectsNewMemberButAllowsReissuingExistingInvitation()
    {
        var scenario = new IdentityScenario();
        var user = scenario.AddUser();
        var (tenant, member, _) = scenario.AddTenant(user);
        member.Deactivate();
        member.RenewInvitation(member.Role);
        var policy = new ProvisionalTenantInvitationPlanPolicy(scenario.Store, new ProvisionalInvitationPlanSettings(1));
        Assert.Equal(InvitationPlanDecision.LimitExceeded, await Evaluate(policy, tenant.Id));
        Assert.Equal(InvitationPlanDecision.Allowed, await Evaluate(policy, tenant.Id, member.Id));
    }

    [Fact]
    public async Task RevokedMemberDoesNotReserveSeatButReinvitationRequiresAvailableSeat()
    {
        var scenario = new IdentityScenario();
        var user = scenario.AddUser();
        var (tenant, member, _) = scenario.AddTenant(user);
        member.Deactivate();
        var policy = new ProvisionalTenantInvitationPlanPolicy(scenario.Store, new ProvisionalInvitationPlanSettings(1));
        Assert.Equal(InvitationPlanDecision.Allowed, await Evaluate(policy, tenant.Id, member.Id));
        var other = scenario.AddUser(email: "other@example.invalid");
        scenario.Store.Memberships.Add(new TenantMembership(Guid.NewGuid(), tenant.Id, other.Id, TenantMembershipRole.Professional, scenario.Clock.GetUtcNow()));
        Assert.Equal(InvitationPlanDecision.LimitExceeded, await Evaluate(policy, tenant.Id, member.Id));
    }

    [Fact]
    public async Task OtherTenantMembershipsDoNotConsumeSeatsAndForeignMembershipIdIsRejected()
    {
        var scenario = new IdentityScenario();
        var user = scenario.AddUser();
        var (tenant, member, _) = scenario.AddTenant(user);
        var (otherTenant, _, _) = scenario.AddTenant(user);
        var policy = new ProvisionalTenantInvitationPlanPolicy(scenario.Store, new ProvisionalInvitationPlanSettings(2));
        Assert.Equal(InvitationPlanDecision.Allowed, await Evaluate(policy, tenant.Id));
        Assert.Equal(InvitationPlanDecision.Unavailable, await Evaluate(policy, otherTenant.Id, member.Id));
    }

    [Fact]
    public async Task SuspendedMembershipStillOccupiesASeat()
    {
        var scenario = new IdentityScenario();
        var (tenant, member, _) = scenario.AddTenant(scenario.AddUser());
        IdentityScenario.LoadSuspendedMembership(member);
        var policy = new ProvisionalTenantInvitationPlanPolicy(scenario.Store, new ProvisionalInvitationPlanSettings(1));
        Assert.Equal(InvitationPlanDecision.LimitExceeded, await Evaluate(policy, tenant.Id));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveLimitIsRejected(int maximumMemberships) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProvisionalInvitationPlanSettings(maximumMemberships));

    private static Task<InvitationPlanDecision> Evaluate(ProvisionalTenantInvitationPlanPolicy policy, Guid tenantId, Guid? membershipId = null) =>
        policy.EvaluateAsync(tenantId, membershipId, TenantMembershipRole.Professional, [], CancellationToken.None);
}
