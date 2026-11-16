using BrothersCare.Domain.Care;

namespace BrothersCare.DomainTests.Care;

public sealed class AppointmentTests
{
    [Fact]
    public void AppointmentNormalizesOptionalNotesAndStartsScheduled()
    {
        var appointment = CreateAppointment(notes: "  Retorno administrativo  ");

        Assert.Equal(AppointmentStatus.Scheduled, appointment.Status);
        Assert.Equal("Retorno administrativo", appointment.Notes);
        Assert.Equal(appointment.CreatedAt, appointment.UpdatedAt);
        Assert.NotNull(appointment.CreatedByMembershipId);
    }

    [Fact]
    public void AppointmentRejectsInvalidOrExcessiveDuration()
    {
        var startsAt = DateTimeOffset.UtcNow.AddDays(1);

        Assert.Throws<ArgumentOutOfRangeException>(() => CreateAppointment(startsAt, startsAt));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateAppointment(startsAt, startsAt.AddHours(25)));
    }

    [Fact]
    public void RescheduleChangesUnitAndRegistersActor()
    {
        var appointment = CreateAppointment();
        var unitId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var updatedAt = appointment.StartsAt.AddHours(-2);

        appointment.Reschedule(unitId, appointment.StartsAt.AddDays(1), appointment.EndsAt.AddDays(1), actorId, updatedAt);

        Assert.Equal(unitId, appointment.ClinicUnitId);
        Assert.Equal(actorId, appointment.UpdatedByMembershipId);
        Assert.Equal(updatedAt, appointment.UpdatedAt);
    }

    [Fact]
    public void CompletedAppointmentCannotTransitionAgain()
    {
        var appointment = CreateAppointment();
        var actorId = Guid.NewGuid();

        appointment.MarkCompleted(actorId, appointment.StartsAt);

        Assert.Equal(AppointmentStatus.Completed, appointment.Status);
        Assert.Throws<InvalidOperationException>(() =>
            appointment.Cancel("Correção", actorId, appointment.StartsAt.AddMinutes(1)));
    }

    [Fact]
    public void AppointmentCannotBeCompletedOrMarkedNoShowBeforeItStarts()
    {
        var appointment = CreateAppointment();
        var actorId = Guid.NewGuid();

        Assert.Throws<InvalidOperationException>(() =>
            appointment.MarkCompleted(actorId, appointment.StartsAt.AddSeconds(-1)));
        Assert.Throws<InvalidOperationException>(() =>
            appointment.MarkNoShow("Paciente ausente", actorId, appointment.StartsAt.AddSeconds(-1)));
    }

    [Fact]
    public void CancellationRequiresReasonAndPreservesAuditData()
    {
        var appointment = CreateAppointment();
        var actorId = Guid.NewGuid();
        var cancelledAt = appointment.StartsAt.AddHours(-1);

        Assert.Throws<ArgumentException>(() => appointment.Cancel(" ", actorId, cancelledAt));

        appointment.Cancel("  Solicitação do paciente  ", actorId, cancelledAt);

        Assert.Equal(AppointmentStatus.Cancelled, appointment.Status);
        Assert.Equal("Solicitação do paciente", appointment.CancellationReason);
        Assert.Equal(actorId, appointment.CancelledByMembershipId);
        Assert.Equal(cancelledAt, appointment.CancelledAt);
    }

    private static Appointment CreateAppointment(
        DateTimeOffset? startsAt = null,
        DateTimeOffset? endsAt = null,
        string? notes = null)
    {
        var start = startsAt ?? DateTimeOffset.UtcNow.AddDays(1);
        return new(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            start, endsAt ?? start.AddHours(1), notes, DateTimeOffset.UtcNow);
    }
}
