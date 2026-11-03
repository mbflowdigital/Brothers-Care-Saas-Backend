using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Application.Tenancy.Contracts;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.Application.Tenancy;

public sealed class ManageClinicUnits(
    ICurrentPrincipalAccessor principalAccessor,
    IClinicUnitStore store,
    TimeProvider timeProvider)
{
    public Task<ClinicUnitResult> CreateAsync(string name, CancellationToken cancellationToken) =>
        ExecuteAsync(name, null, false, cancellationToken);

    public Task<ClinicUnitResult> RenameAsync(Guid clinicUnitId, string name, CancellationToken cancellationToken) =>
        ExecuteAsync(name, clinicUnitId, true, cancellationToken);

    public async Task<ClinicUnitDeactivationResult> DeactivateAsync(Guid clinicUnitId, CancellationToken cancellationToken)
    {
        if (clinicUnitId == Guid.Empty)
        {
            return new(ClinicUnitOperationStatus.InvalidInput);
        }

        var context = await GetAuthorizedContextAsync(cancellationToken);
        if (context.Status != ClinicUnitOperationStatus.Success || context.Transaction is null)
        {
            return new(context.Status);
        }

        await using var transaction = context.Transaction;
        var unit = await transaction.FindTrackedAsync(context.TenantId!.Value, clinicUnitId, cancellationToken);
        if (unit is null)
        {
            return new(ClinicUnitOperationStatus.NotFound);
        }

        if (!unit.IsActive)
        {
            return new(ClinicUnitOperationStatus.AlreadyInactive);
        }

        if (unit.IsDefault)
        {
            return new(ClinicUnitOperationStatus.DefaultUnitCannotBeDeactivated);
        }

        var appointments = await transaction.FindFutureScheduledAppointmentsAsync(
            context.TenantId.Value,
            clinicUnitId,
            timeProvider.GetUtcNow(),
            cancellationToken);
        foreach (var appointment in appointments)
        {
            appointment.Cancel();
        }

        unit.Deactivate();
        await transaction.CommitAsync(cancellationToken);
        return new(ClinicUnitOperationStatus.Success,
            new(unit.Id, unit.Name, unit.IsActive, unit.CreatedAt),
            appointments.Count);
    }

    public async Task<ClinicUnitQueryResult> ListAsync(
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (!IsValidPage(page, pageSize))
        {
            return new(ClinicUnitOperationStatus.InvalidInput);
        }

        var context = await GetAuthorizedContextAsync(cancellationToken);
        if (context.Status != ClinicUnitOperationStatus.Success || context.Transaction is null)
        {
            return new(context.Status);
        }

        await using var transaction = context.Transaction;
        var result = await transaction.ListAsync(context.TenantId!.Value, NormalizeOptional(search), page, pageSize, cancellationToken);
        return new(ClinicUnitOperationStatus.Success, result);
    }

    public async Task<ClinicUnitDetailsResult> GetAsync(Guid clinicUnitId, CancellationToken cancellationToken)
    {
        if (clinicUnitId == Guid.Empty)
        {
            return new(ClinicUnitOperationStatus.InvalidInput);
        }

        var context = await GetAuthorizedContextAsync(cancellationToken);
        if (context.Status != ClinicUnitOperationStatus.Success || context.Transaction is null)
        {
            return new(context.Status);
        }

        await using var transaction = context.Transaction;
        var unit = await transaction.FindAsync(context.TenantId!.Value, clinicUnitId, cancellationToken);
        return unit is null
            ? new(ClinicUnitOperationStatus.NotFound)
            : new(ClinicUnitOperationStatus.Success, unit);
    }

    private async Task<ClinicUnitResult> ExecuteAsync(
        string name,
        Guid? clinicUnitId,
        bool rename,
        CancellationToken cancellationToken)
    {
        if (!IsValidName(name) || (rename && clinicUnitId == Guid.Empty))
        {
            return new(ClinicUnitOperationStatus.InvalidInput);
        }

        var context = await GetAuthorizedContextAsync(cancellationToken);
        if (context.Status != ClinicUnitOperationStatus.Success || context.Transaction is null)
        {
            return new(context.Status);
        }

        await using var transaction = context.Transaction;
        var tenantId = context.TenantId!.Value;
        var normalizedName = name.Trim();
        if (await transaction.NameExistsAsync(tenantId, normalizedName, clinicUnitId, cancellationToken))
        {
            return new(ClinicUnitOperationStatus.NameAlreadyExists);
        }

        ClinicUnit unit;
        if (rename)
        {
            var existing = await transaction.FindTrackedAsync(tenantId, clinicUnitId!.Value, cancellationToken);
            if (existing is null)
            {
                return new(ClinicUnitOperationStatus.NotFound);
            }

            unit = existing;
            unit.Rename(normalizedName);
        }
        else
        {
            unit = new ClinicUnit(Guid.NewGuid(), tenantId, normalizedName, timeProvider.GetUtcNow());
            transaction.Add(unit);
        }

        await transaction.CommitAsync(cancellationToken);

        return new(ClinicUnitOperationStatus.Success, new(unit.Id, unit.Name, unit.IsActive, unit.CreatedAt));
    }

    private async Task<AuthorizedClinicUnitContext> GetAuthorizedContextAsync(CancellationToken cancellationToken)
    {
        var principal = principalAccessor.Principal;
        if (principal?.ActiveTenantId is not { } tenantId)
        {
            return new(ClinicUnitOperationStatus.Forbidden);
        }

        var transaction = await store.BeginAsync(cancellationToken);
        var session = await transaction.FindSessionAsync(principal.SessionId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (session is null || session.UserId != principal.UserId || !session.IsValidAt(now) ||
            !await transaction.HasActiveCredentialAsync(principal.UserId, cancellationToken))
        {
            await transaction.DisposeAsync();
            return new(ClinicUnitOperationStatus.Unauthenticated);
        }

        if (session.ActiveTenantId != tenantId ||
            !await transaction.HasActiveClinicAdministratorAsync(tenantId, principal.UserId, cancellationToken))
        {
            await transaction.DisposeAsync();
            return new(ClinicUnitOperationStatus.Forbidden);
        }

        return new(ClinicUnitOperationStatus.Success, tenantId, transaction);
    }

    private static bool IsValidName(string name) => !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 200;

    private static bool IsValidPage(int page, int pageSize) => page >= 1 && pageSize is >= 1 and <= 100;

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public enum ClinicUnitOperationStatus
{
    Success = 1,
    Unauthenticated = 2,
    Forbidden = 3,
    NotFound = 4,
    InvalidInput = 5,
    NameAlreadyExists = 6,
    AlreadyInactive = 7,
    DefaultUnitCannotBeDeactivated = 8,
}

public sealed record ClinicUnitItem(Guid Id, string Name, bool IsActive, DateTimeOffset CreatedAt);

public sealed record ClinicUnitDetails(Guid Id, string Name, bool IsActive, DateTimeOffset CreatedAt);

public sealed record ClinicUnitPage(IReadOnlyList<ClinicUnitItem> Items, int Page, int PageSize, int TotalItems, int TotalPages);

public sealed record ClinicUnitResult(ClinicUnitOperationStatus Status, ClinicUnitItem? Unit = null);

public sealed record ClinicUnitQueryResult(ClinicUnitOperationStatus Status, ClinicUnitPage? Page = null);

public sealed record ClinicUnitDetailsResult(ClinicUnitOperationStatus Status, ClinicUnitDetails? Unit = null);

public sealed record ClinicUnitDeactivationResult(
    ClinicUnitOperationStatus Status,
    ClinicUnitItem? Unit = null,
    int CancelledAppointments = 0);

internal sealed record AuthorizedClinicUnitContext(
    ClinicUnitOperationStatus Status,
    Guid? TenantId = null,
    IClinicUnitTransaction? Transaction = null);
