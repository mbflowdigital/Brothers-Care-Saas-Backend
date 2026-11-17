using BrothersCare.Application.Care;
using BrothersCare.Application.Care.Contracts;
using BrothersCare.Application.Identity.Contracts;
using BrothersCare.ApplicationTests.Support;
using BrothersCare.Domain.Care;
using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.ApplicationTests.Care;

public sealed class ManageClinicalRecordsTests
{
    [Fact]
    public async Task CreatesEmptyDraftWithoutAppointmentAndPreservesRealAuthor()
    {
        var scenario = new Scenario();

        var result = await scenario.Service.CreateAsync(
            new(scenario.PatientId, scenario.UnitId, null, null, null), default);

        Assert.Equal(ClinicalRecordOperationStatus.Success, result.Status);
        Assert.Equal(ClinicalRecordStatus.Draft.ToString(), result.Record!.Status);
        Assert.Null(result.Record.AppointmentId);
        Assert.Equal(scenario.MembershipId, result.Record.AuthorMembershipId);
    }

    [Fact]
    public async Task UpdatesDraftWithOptimisticVersion()
    {
        var scenario = new Scenario();
        var created = await scenario.CreateAsync();

        var result = await scenario.Service.UpdateAsync(created.Id,
            new("Relato", "Procedimento", "Conduta", created.Version), default);

        Assert.Equal(ClinicalRecordOperationStatus.Success, result.Status);
        Assert.Equal("Procedimento", result.Record!.ProceduresAndObservations);
        Assert.NotEqual(created.Version, result.Record.Version);
    }

    [Fact]
    public async Task RejectsStaleUpdate()
    {
        var scenario = new Scenario();
        var created = await scenario.CreateAsync();

        var result = await scenario.Service.UpdateAsync(created.Id,
            new(null, "Procedimento", "Conduta", Convert.ToBase64String([99])), default);

        Assert.Equal(ClinicalRecordOperationStatus.Conflict, result.Status);
    }

    [Fact]
    public async Task RejectsInvalidFinalizationAndFinalizesCompleteDraft()
    {
        var scenario = new Scenario();
        var empty = await scenario.CreateAsync();
        var invalid = await scenario.Service.FinalizeAsync(empty.Id, new(empty.Version), default);
        Assert.Equal(ClinicalRecordOperationStatus.FinalizationInvalid, invalid.Status);

        var updated = await scenario.Service.UpdateAsync(empty.Id,
            new(null, "Procedimento", "Conduta", empty.Version), default);
        var finalized = await scenario.Service.FinalizeAsync(empty.Id, new(updated.Record!.Version), default);
        Assert.Equal(ClinicalRecordOperationStatus.Success, finalized.Status);
        Assert.Equal(ClinicalRecordStatus.Finalized.ToString(), finalized.Record!.Status);
        Assert.NotNull(finalized.Record.FinalizedAt);
    }

    [Fact]
    public async Task BlocksEditingAfterFinalization()
    {
        var scenario = new Scenario();
        var draft = await scenario.CreateAsync("Procedimento", "Conduta");
        var finalized = await scenario.Service.FinalizeAsync(draft.Id, new(draft.Version), default);

        var result = await scenario.Service.UpdateAsync(draft.Id,
            new(null, "Alterado", "Alterado", finalized.Record!.Version), default);

        Assert.Equal(ClinicalRecordOperationStatus.Finalized, result.Status);
    }

    [Fact]
    public async Task AllowsSeveralRecordsForTheSamePatientAndReturnsNewestFirst()
    {
        var scenario = new Scenario();
        await scenario.CreateAsync();
        scenario.Clock.Advance(TimeSpan.FromMinutes(1));
        var newest = await scenario.CreateAsync();

        var result = await scenario.Service.ListAsync(scenario.PatientId, default);

        Assert.Equal(2, result.Items!.Count);
        Assert.Equal(newest.Id, result.Items[0].Id);
    }

    [Fact]
    public async Task BlocksProfessionalWithoutCareAssignment()
    {
        var scenario = new Scenario { HasCareAccess = false };

        var result = await scenario.Service.CreateAsync(
            new(scenario.PatientId, scenario.UnitId, null, null, null), default);

        Assert.Equal(ClinicalRecordOperationStatus.Forbidden, result.Status);
        Assert.Empty(scenario.Records);
    }

    [Fact]
    public async Task BlocksUpdatingAndFinalizingAfterCareAssignmentIsRevoked()
    {
        var scenario = new Scenario();
        var draft = await scenario.CreateAsync("Procedimento", "Conduta");
        scenario.HasCareAccess = false;

        var update = await scenario.Service.UpdateAsync(draft.Id,
            new(null, "Alterado", "Alterado", draft.Version), default);
        var finalization = await scenario.Service.FinalizeAsync(draft.Id, new(draft.Version), default);

        Assert.Equal(ClinicalRecordOperationStatus.Forbidden, update.Status);
        Assert.Equal(ClinicalRecordOperationStatus.Forbidden, finalization.Status);
    }

    [Fact]
    public async Task LinksOneEvolutionToACompletedAppointment()
    {
        var scenario = new Scenario();
        var appointmentId = Guid.NewGuid();
        scenario.Appointment = new(appointmentId, scenario.PatientId, scenario.UnitId,
            scenario.ProfessionalId, AppointmentStatus.Completed);

        var created = await scenario.Service.CreateAsync(
            new(scenario.PatientId, scenario.UnitId, null, null, null, appointmentId), default);
        var duplicate = await scenario.Service.CreateAsync(
            new(scenario.PatientId, scenario.UnitId, null, null, null, appointmentId), default);

        Assert.Equal(ClinicalRecordOperationStatus.Success, created.Status);
        Assert.Equal(appointmentId, created.Record!.AppointmentId);
        Assert.Equal(ClinicalRecordOperationStatus.AppointmentAlreadyLinked, duplicate.Status);
    }

    [Fact]
    public async Task RejectsEvolutionForAppointmentThatIsNotCompleted()
    {
        var scenario = new Scenario();
        var appointmentId = Guid.NewGuid();
        scenario.Appointment = new(appointmentId, scenario.PatientId, scenario.UnitId,
            scenario.ProfessionalId, AppointmentStatus.Scheduled);

        var result = await scenario.Service.CreateAsync(
            new(scenario.PatientId, scenario.UnitId, null, null, null, appointmentId), default);

        Assert.Equal(ClinicalRecordOperationStatus.AppointmentNotCompleted, result.Status);
        Assert.Empty(scenario.Records);
    }

    [Fact]
    public async Task DoesNotReturnRecordFromAnotherTenant()
    {
        var scenario = new Scenario();
        var foreign = new ClinicalRecord(Guid.NewGuid(), Guid.NewGuid(), scenario.PatientId,
            scenario.UnitId, scenario.MembershipId, null, null, null, null, scenario.Clock.GetUtcNow());
        scenario.Records.Add(foreign);

        var result = await scenario.Service.GetAsync(foreign.Id, default);

        Assert.Equal(ClinicalRecordOperationStatus.NotFound, result.Status);
    }

    private sealed class Scenario : ICurrentPrincipalAccessor, IClinicalRecordStore, IClinicalRecordTransaction
    {
        private int version;
        public TestClock Clock { get; } = new();
        public Guid TenantId { get; } = Guid.NewGuid();
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid SessionId { get; } = Guid.NewGuid();
        public Guid MembershipId { get; } = Guid.NewGuid();
        public Guid ProfessionalId { get; } = Guid.NewGuid();
        public Guid PatientId { get; } = Guid.NewGuid();
        public Guid UnitId { get; } = Guid.NewGuid();
        public bool HasCareAccess { get; set; } = true;
        public ClinicalAppointmentContext? Appointment { get; set; }
        public List<ClinicalRecord> Records { get; } = [];
        public CurrentPrincipal? Principal { get; }
        public ManageClinicalRecords Service { get; }
        private UserSession Session { get; }

        public Scenario()
        {
            var membership = new TenantMembership(MembershipId, TenantId, UserId,
                TenantMembershipRole.Professional, Clock.GetUtcNow());
            Session = new UserSession(SessionId, UserId, new byte[32], Clock.GetUtcNow(),
                Clock.GetUtcNow().AddHours(1), Clock.GetUtcNow().AddHours(2));
            Session.SelectActiveTenant(membership, Clock.GetUtcNow());
            Principal = new(UserId, SessionId, TenantId, null, MembershipId, "Professional", [UnitId]);
            Service = new(this, this, Clock);
        }

        public async Task<ClinicalRecordDetails> CreateAsync(string? procedures = null, string? plan = null)
        {
            var result = await Service.CreateAsync(new(PatientId, UnitId, null, procedures, plan), default);
            return result.Record!;
        }

        public Task<IClinicalRecordTransaction> BeginAsync(CancellationToken cancellationToken) => Task.FromResult<IClinicalRecordTransaction>(this);
        public Task<UserSession?> FindSessionAsync(Guid sessionId, CancellationToken cancellationToken) => Task.FromResult<UserSession?>(Session);
        public Task<bool> HasActiveCredentialAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<ClinicalAuthorContext?> FindAuthorContextAsync(Guid tenantId, Guid userId, Guid membershipId, CancellationToken cancellationToken) =>
            Task.FromResult<ClinicalAuthorContext?>(tenantId == TenantId && userId == UserId && membershipId == MembershipId
                ? new(MembershipId, ProfessionalId, "Profissional", "Fisioterapia") : null);
        public Task<bool> HasCareAccessAsync(Guid tenantId, Guid professionalId, Guid patientId, Guid clinicUnitId, CancellationToken cancellationToken) =>
            Task.FromResult(HasCareAccess && tenantId == TenantId && professionalId == ProfessionalId && patientId == PatientId && clinicUnitId == UnitId);
        public Task<ClinicalAppointmentContext?> FindAppointmentContextAsync(Guid tenantId, Guid appointmentId, CancellationToken cancellationToken) =>
            Task.FromResult(Appointment is not null && tenantId == TenantId && appointmentId == Appointment.Id
                ? Appointment : null);
        public Task<bool> HasRecordForAppointmentAsync(Guid tenantId, Guid appointmentId, CancellationToken cancellationToken) =>
            Task.FromResult(Records.Any(item => item.TenantId == tenantId && item.AppointmentId == appointmentId));
        public Task<ClinicalRecord?> FindTrackedAsync(Guid tenantId, Guid recordId, CancellationToken cancellationToken) =>
            Task.FromResult(Records.SingleOrDefault(item => item.TenantId == tenantId && item.Id == recordId));
        public Task<ClinicalRecordDetails?> GetAsync(Guid tenantId, Guid recordId, CancellationToken cancellationToken) =>
            Task.FromResult(Details(Records.SingleOrDefault(item => item.TenantId == tenantId && item.Id == recordId)));
        public Task<IReadOnlyList<ClinicalRecordSummary>> ListAsync(Guid tenantId, Guid professionalId, Guid patientId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ClinicalRecordSummary>>(Records.Where(item => item.TenantId == tenantId && item.PatientId == patientId)
                .OrderByDescending(item => item.CreatedAt).Select(item => new ClinicalRecordSummary(item.Id, item.PatientId,
                    "Paciente", item.ClinicUnitId, "Unidade", "Profissional", "Fisioterapia", item.Status.ToString(),
                    item.ProceduresAndObservations, item.CreatedAt, item.UpdatedAt, item.FinalizedAt)).ToArray());
        public Task<ClinicalRecordOptions> ListOptionsAsync(Guid tenantId, Guid professionalId, CancellationToken cancellationToken) =>
            Task.FromResult(new ClinicalRecordOptions(HasCareAccess ? [new(PatientId, "Paciente", [UnitId])] : [],
                HasCareAccess ? [new(UnitId, "Unidade")] : [], new(MembershipId, ProfessionalId, "Profissional", "Fisioterapia")));
        public void Add(ClinicalRecord record) => Records.Add(record);
        public Task<bool> SaveAsync(CancellationToken cancellationToken)
        {
            foreach (var record in Records)
                typeof(ClinicalRecord).GetProperty(nameof(ClinicalRecord.Version))!.SetValue(record, new[] { (byte)++version });
            return Task.FromResult(true);
        }
        public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static ClinicalRecordDetails? Details(ClinicalRecord? item) => item is null ? null : new(item.Id,
            item.PatientId, "Paciente", item.ClinicUnitId, "Unidade", item.AuthorMembershipId, "Profissional",
            "Fisioterapia", item.AppointmentId, item.PatientReport, item.ProceduresAndObservations,
            item.AssessmentAndPlan, item.Status.ToString(), item.CreatedAt, item.UpdatedAt, item.FinalizedAt,
            Convert.ToBase64String(item.Version));
    }
}
