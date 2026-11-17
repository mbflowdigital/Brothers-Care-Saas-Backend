using BrothersCare.Domain.Care;

namespace BrothersCare.DomainTests.Care;

public sealed class ClinicalRecordTests
{
    private static ClinicalRecord Create(string? procedures = null, string? plan = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            null, procedures, plan, DateTimeOffset.UtcNow);

    [Fact]
    public void DraftAcceptsEmptyClinicalSectionsAndNoAppointment()
    {
        var record = Create();

        Assert.Equal(ClinicalRecordStatus.Draft, record.Status);
        Assert.Null(record.AppointmentId);
        Assert.Empty(record.PatientReport);
    }

    [Fact]
    public void UpdateTrimsSections()
    {
        var record = Create();

        record.Update(" relato ", " procedimento ", " plano ", DateTimeOffset.UtcNow);

        Assert.Equal("relato", record.PatientReport);
        Assert.Equal("procedimento", record.ProceduresAndObservations);
        Assert.Equal("plano", record.AssessmentAndPlan);
    }

    [Fact]
    public void FinalizationRequiresProceduresAndAssessment()
    {
        var record = Create();

        Assert.Throws<InvalidOperationException>(() => record.Finalize(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void FinalizedRecordCannotBeEditedOrFinalizedAgain()
    {
        var record = Create("Procedimentos", "Conduta");
        record.Finalize(DateTimeOffset.UtcNow);

        Assert.Equal(ClinicalRecordStatus.Finalized, record.Status);
        Assert.Throws<InvalidOperationException>(() => record.Update(null, "Outro", "Outro", DateTimeOffset.UtcNow));
        Assert.Throws<InvalidOperationException>(() => record.Finalize(DateTimeOffset.UtcNow));
    }
}
