using BrothersCare.Application.Care.Contracts;
using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Domain.Care;

namespace BrothersCare.Application.Care;

public sealed class ManageClinicalRecords(
    ICurrentPrincipalAccessor principalAccessor,
    IClinicalRecordStore store,
    TimeProvider timeProvider)
{
    public async Task<ClinicalRecordResult> GetOptionsAsync(CancellationToken cancellationToken)
    {
        await using var access = await AuthorizeAsync(cancellationToken);
        return access.Status == ClinicalRecordOperationStatus.Success
            ? new(access.Status, Options: await access.Transaction!.ListOptionsAsync(
                access.TenantId, access.Author!.ProfessionalId, cancellationToken))
            : new(access.Status);
    }

    public async Task<ClinicalRecordResult> ListAsync(Guid patientId, CancellationToken cancellationToken)
    {
        if (patientId == Guid.Empty) return new(ClinicalRecordOperationStatus.InvalidInput);
        await using var access = await AuthorizeAsync(cancellationToken);
        if (access.Status != ClinicalRecordOperationStatus.Success) return new(access.Status);
        if (!await HasAccessAsync(access, patientId, null, cancellationToken))
            return new(ClinicalRecordOperationStatus.Forbidden);
        return new(ClinicalRecordOperationStatus.Success, Items: await access.Transaction!.ListAsync(
            access.TenantId, access.Author!.ProfessionalId, patientId, cancellationToken));
    }

    public async Task<ClinicalRecordResult> GetAsync(Guid recordId, CancellationToken cancellationToken)
    {
        if (recordId == Guid.Empty) return new(ClinicalRecordOperationStatus.InvalidInput);
        await using var access = await AuthorizeAsync(cancellationToken);
        if (access.Status != ClinicalRecordOperationStatus.Success) return new(access.Status);
        var record = await access.Transaction!.GetAsync(access.TenantId, recordId, cancellationToken);
        if (record is null) return new(ClinicalRecordOperationStatus.NotFound);
        if (!await HasAccessAsync(access, record.PatientId, record.ClinicUnitId, cancellationToken))
            return new(ClinicalRecordOperationStatus.Forbidden);
        return new(ClinicalRecordOperationStatus.Success, Record: record);
    }

    public async Task<ClinicalRecordResult> CreateAsync(
        ClinicalRecordInput input,
        CancellationToken cancellationToken)
    {
        if (!IsValid(input)) return new(ClinicalRecordOperationStatus.InvalidInput);
        await using var access = await AuthorizeAsync(cancellationToken);
        if (access.Status != ClinicalRecordOperationStatus.Success) return new(access.Status);
        if (!await HasAccessAsync(access, input.PatientId, input.ClinicUnitId, cancellationToken))
            return new(ClinicalRecordOperationStatus.Forbidden);

        if (input.AppointmentId is { } appointmentId)
        {
            var appointment = await access.Transaction!.FindAppointmentContextAsync(
                access.TenantId, appointmentId, cancellationToken);
            if (appointment is null || appointment.PatientId != input.PatientId ||
                appointment.ClinicUnitId != input.ClinicUnitId ||
                appointment.ProfessionalId != access.Author!.ProfessionalId)
                return new(ClinicalRecordOperationStatus.Forbidden);
            if (appointment.Status != AppointmentStatus.Completed)
                return new(ClinicalRecordOperationStatus.AppointmentNotCompleted);
            if (await access.Transaction.HasRecordForAppointmentAsync(
                    access.TenantId, appointmentId, cancellationToken))
                return new(ClinicalRecordOperationStatus.AppointmentAlreadyLinked);
        }

        var now = timeProvider.GetUtcNow();
        ClinicalRecord record;
        try
        {
            record = new ClinicalRecord(Guid.NewGuid(), access.TenantId, input.PatientId, input.ClinicUnitId,
                access.Author!.MembershipId, input.AppointmentId, input.PatientReport, input.ProceduresAndObservations,
                input.AssessmentAndPlan, now);
        }
        catch (ArgumentException)
        {
            return new(ClinicalRecordOperationStatus.InvalidInput);
        }

        access.Transaction!.Add(record);
        if (!await access.Transaction.SaveAsync(cancellationToken))
            return new(ClinicalRecordOperationStatus.Conflict);
        await access.Transaction.CommitAsync(cancellationToken);
        return new(ClinicalRecordOperationStatus.Success,
            Record: await ReadCreatedAsync(access.Transaction, access.TenantId, record.Id, cancellationToken));
    }

    public async Task<ClinicalRecordResult> UpdateAsync(
        Guid recordId,
        ClinicalRecordUpdateInput input,
        CancellationToken cancellationToken)
    {
        if (recordId == Guid.Empty || !IsValid(input)) return new(ClinicalRecordOperationStatus.InvalidInput);
        await using var access = await AuthorizeAsync(cancellationToken);
        if (access.Status != ClinicalRecordOperationStatus.Success) return new(access.Status);
        var record = await access.Transaction!.FindTrackedAsync(access.TenantId, recordId, cancellationToken);
        if (record is null) return new(ClinicalRecordOperationStatus.NotFound);
        if (!await HasAccessAsync(access, record.PatientId, record.ClinicUnitId, cancellationToken))
            return new(ClinicalRecordOperationStatus.Forbidden);
        if (record.AuthorMembershipId != access.Author!.MembershipId)
            return new(ClinicalRecordOperationStatus.Forbidden);
        if (record.Status == ClinicalRecordStatus.Finalized)
            return new(ClinicalRecordOperationStatus.Finalized);
        if (!VersionMatches(record.Version, input.Version))
            return new(ClinicalRecordOperationStatus.Conflict);
        try
        {
            record.Update(input.PatientReport, input.ProceduresAndObservations, input.AssessmentAndPlan,
                timeProvider.GetUtcNow());
        }
        catch (ArgumentException)
        {
            return new(ClinicalRecordOperationStatus.InvalidInput);
        }

        if (!await access.Transaction.SaveAsync(cancellationToken))
            return new(ClinicalRecordOperationStatus.Conflict);
        await access.Transaction.CommitAsync(cancellationToken);
        return new(ClinicalRecordOperationStatus.Success,
            Record: await ReadCreatedAsync(access.Transaction, access.TenantId, record.Id, cancellationToken));
    }

    public async Task<ClinicalRecordResult> FinalizeAsync(
        Guid recordId,
        ClinicalRecordFinalizationInput input,
        CancellationToken cancellationToken)
    {
        if (recordId == Guid.Empty || string.IsNullOrWhiteSpace(input.Version))
            return new(ClinicalRecordOperationStatus.InvalidInput);
        await using var access = await AuthorizeAsync(cancellationToken);
        if (access.Status != ClinicalRecordOperationStatus.Success) return new(access.Status);
        var record = await access.Transaction!.FindTrackedAsync(access.TenantId, recordId, cancellationToken);
        if (record is null) return new(ClinicalRecordOperationStatus.NotFound);
        if (!await HasAccessAsync(access, record.PatientId, record.ClinicUnitId, cancellationToken))
            return new(ClinicalRecordOperationStatus.Forbidden);
        if (record.AuthorMembershipId != access.Author!.MembershipId)
            return new(ClinicalRecordOperationStatus.Forbidden);
        if (record.Status == ClinicalRecordStatus.Finalized)
            return new(ClinicalRecordOperationStatus.Finalized);
        if (!VersionMatches(record.Version, input.Version))
            return new(ClinicalRecordOperationStatus.Conflict);
        try
        {
            record.Finalize(timeProvider.GetUtcNow());
        }
        catch (InvalidOperationException)
        {
            return new(ClinicalRecordOperationStatus.FinalizationInvalid);
        }

        if (!await access.Transaction.SaveAsync(cancellationToken))
            return new(ClinicalRecordOperationStatus.Conflict);
        await access.Transaction.CommitAsync(cancellationToken);
        return new(ClinicalRecordOperationStatus.Success,
            Record: await ReadCreatedAsync(access.Transaction, access.TenantId, record.Id, cancellationToken));
    }

    private async Task<ClinicalAccess> AuthorizeAsync(CancellationToken cancellationToken)
    {
        var principal = principalAccessor.Principal;
        if (principal?.ActiveTenantId is not { } tenantId || principal.TenantMembershipId is not { } membershipId)
            return new(ClinicalRecordOperationStatus.Forbidden);
        var transaction = await store.BeginAsync(cancellationToken);
        var session = await transaction.FindSessionAsync(principal.SessionId, cancellationToken);
        if (session is null || session.UserId != principal.UserId || !session.IsValidAt(timeProvider.GetUtcNow()) ||
            session.ActiveTenantId != tenantId ||
            !await transaction.HasActiveCredentialAsync(principal.UserId, cancellationToken))
        {
            await transaction.DisposeAsync();
            return new(ClinicalRecordOperationStatus.Unauthenticated);
        }

        var author = await transaction.FindAuthorContextAsync(
            tenantId, principal.UserId, membershipId, cancellationToken);
        if (author is null)
        {
            await transaction.DisposeAsync();
            return new(ClinicalRecordOperationStatus.Forbidden);
        }

        return new(ClinicalRecordOperationStatus.Success, tenantId, author, transaction);
    }

    private static async Task<bool> HasAccessAsync(
        ClinicalAccess access, Guid patientId, Guid? clinicUnitId, CancellationToken cancellationToken)
    {
        if (clinicUnitId is { } unitId)
            return await access.Transaction!.HasCareAccessAsync(
                access.TenantId, access.Author!.ProfessionalId, patientId, unitId, cancellationToken);
        var options = await access.Transaction!.ListOptionsAsync(
            access.TenantId, access.Author!.ProfessionalId, cancellationToken);
        return options.Patients.Any(patient => patient.Id == patientId);
    }

    private static bool IsValid(ClinicalRecordInput input) =>
        input.PatientId != Guid.Empty && input.ClinicUnitId != Guid.Empty &&
        SectionsFit(input.PatientReport, input.ProceduresAndObservations, input.AssessmentAndPlan);

    private static bool IsValid(ClinicalRecordUpdateInput input) =>
        !string.IsNullOrWhiteSpace(input.Version) &&
        SectionsFit(input.PatientReport, input.ProceduresAndObservations, input.AssessmentAndPlan);

    private static bool SectionsFit(params string?[] sections) =>
        sections.All(section => section is null || section.Trim().Length <= ClinicalRecord.MaximumSectionLength);

    private static bool VersionMatches(byte[] current, string supplied)
    {
        try { return current.AsSpan().SequenceEqual(Convert.FromBase64String(supplied)); }
        catch (FormatException) { return false; }
    }

    private static Task<ClinicalRecordDetails?> ReadCreatedAsync(
        IClinicalRecordTransaction transaction, Guid tenantId, Guid recordId, CancellationToken cancellationToken) =>
        transaction.GetAsync(tenantId, recordId, cancellationToken);

    private sealed record ClinicalAccess(
        ClinicalRecordOperationStatus Status,
        Guid TenantId = default,
        ClinicalAuthorContext? Author = null,
        IClinicalRecordTransaction? Transaction = null) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Transaction?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
}

public sealed record ClinicalRecordInput(
    Guid PatientId,
    Guid ClinicUnitId,
    string? PatientReport,
    string? ProceduresAndObservations,
    string? AssessmentAndPlan,
    Guid? AppointmentId = null);

public sealed record ClinicalRecordUpdateInput(
    string? PatientReport,
    string? ProceduresAndObservations,
    string? AssessmentAndPlan,
    string Version);

public sealed record ClinicalRecordFinalizationInput(string Version);

public sealed record ClinicalRecordResult(
    ClinicalRecordOperationStatus Status,
    ClinicalRecordDetails? Record = null,
    IReadOnlyList<ClinicalRecordSummary>? Items = null,
    ClinicalRecordOptions? Options = null);

public enum ClinicalRecordOperationStatus
{
    Success = 1,
    Unauthenticated = 2,
    Forbidden = 3,
    InvalidInput = 4,
    NotFound = 5,
    Finalized = 6,
    FinalizationInvalid = 7,
    Conflict = 8,
    AppointmentNotCompleted = 9,
    AppointmentAlreadyLinked = 10,
}
