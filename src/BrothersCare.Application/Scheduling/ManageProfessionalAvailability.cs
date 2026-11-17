using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Application.Scheduling.Contracts;
using BrothersCare.Domain.Care;

namespace BrothersCare.Application.Scheduling;

public sealed class GetProfessionalAvailability(
    ICurrentPrincipalAccessor principalAccessor,
    IAppointmentStore store,
    TimeProvider timeProvider)
{
    public async Task<ProfessionalAvailabilityResult> ExecuteAsync(
        Guid professionalId,
        Guid clinicUnitId,
        DateTimeOffset from,
        DateTimeOffset through,
        CancellationToken cancellationToken)
    {
        if (professionalId == Guid.Empty || clinicUnitId == Guid.Empty ||
            from >= through || through - from > TimeSpan.FromDays(366))
        {
            return new(ProfessionalAvailabilityStatus.InvalidInput);
        }

        await using var transaction = await store.BeginAsync(cancellationToken);
        var access = await SchedulingAccess.ResolveAvailabilityManagerAsync(
            principalAccessor.Principal, professionalId, transaction, timeProvider, cancellationToken);
        if (access.Status != SchedulingAccessStatus.Success)
        {
            return new(MapAccessStatus(access.Status));
        }

        var scope = access.Scope
            ?? throw new InvalidOperationException("Successful availability access must include its scope.");
        if (!await transaction.IsActiveProfessionalUnitLinkedAsync(
                scope.TenantId, professionalId, clinicUnitId, cancellationToken))
        {
            return new(ProfessionalAvailabilityStatus.ClinicUnitUnavailable);
        }

        var units = await transaction.ListClinicUnitOptionsAsync(
            scope.TenantId, scope.Professional.MembershipId, cancellationToken);
        var unit = units.SingleOrDefault(item => item.Id == clinicUnitId)
            ?? throw new InvalidOperationException("A linked active unit must have an availability option.");
        var windows = await transaction.ListAvailabilityWindowsAsync(
            scope.TenantId, professionalId, clinicUnitId, cancellationToken);
        var blocks = await transaction.ListAvailabilityBlocksAsync(
            scope.TenantId, professionalId, from, through, cancellationToken);

        return new(ProfessionalAvailabilityStatus.Success, new(
            professionalId,
            clinicUnitId,
            unit.TimeZoneId,
            windows.Count > 0,
            windows.Select(ToWindowItem).ToArray(),
            blocks.Select(ToBlockItem).ToArray()));
    }

    internal static ProfessionalAvailabilityStatus MapAccessStatus(SchedulingAccessStatus status) =>
        status switch
        {
            SchedulingAccessStatus.Unauthenticated => ProfessionalAvailabilityStatus.Unauthenticated,
            SchedulingAccessStatus.Forbidden => ProfessionalAvailabilityStatus.Forbidden,
            SchedulingAccessStatus.ProfessionalNotFound => ProfessionalAvailabilityStatus.ProfessionalNotFound,
            _ => throw new InvalidOperationException($"Unsupported scheduling access status: {status}."),
        };

    internal static ProfessionalAvailabilityWindowItem ToWindowItem(ProfessionalAvailabilityWindow window) =>
        new(window.Id, window.ClinicUnitId, window.DayOfWeek, window.StartsAt, window.EndsAt);

    internal static ProfessionalAvailabilityBlockItem ToBlockItem(ProfessionalAvailabilityBlock block) =>
        new(block.Id, block.StartsAt, block.EndsAt, block.Kind.ToString(), block.Reason);
}

public sealed class SetProfessionalWeeklyAvailability(
    ICurrentPrincipalAccessor principalAccessor,
    IAppointmentStore store,
    TimeProvider timeProvider)
{
    public async Task<ProfessionalAvailabilityStatus> ExecuteAsync(
        SetProfessionalWeeklyAvailabilityCommand command,
        CancellationToken cancellationToken)
    {
        if (!IsValid(command))
        {
            return ProfessionalAvailabilityStatus.InvalidInput;
        }

        await using var transaction = await store.BeginAsync(cancellationToken);
        var access = await SchedulingAccess.ResolveAvailabilityManagerAsync(
            principalAccessor.Principal, command.ProfessionalId, transaction, timeProvider, cancellationToken);
        if (access.Status != SchedulingAccessStatus.Success)
        {
            return GetProfessionalAvailability.MapAccessStatus(access.Status);
        }

        var scope = access.Scope
            ?? throw new InvalidOperationException("Successful availability access must include its scope.");
        if (!await transaction.IsActiveProfessionalUnitLinkedAsync(
                scope.TenantId, command.ProfessionalId, command.ClinicUnitId, cancellationToken))
        {
            return ProfessionalAvailabilityStatus.ClinicUnitUnavailable;
        }

        var createdAt = timeProvider.GetUtcNow();
        var windows = command.Windows.Select(window => new ProfessionalAvailabilityWindow(
            Guid.NewGuid(),
            scope.TenantId,
            command.ProfessionalId,
            command.ClinicUnitId,
            window.DayOfWeek,
            window.StartsAt,
            window.EndsAt,
            createdAt)).ToArray();

        await transaction.ReplaceAvailabilityWindowsAsync(
            scope.TenantId, command.ProfessionalId, command.ClinicUnitId, windows, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ProfessionalAvailabilityStatus.Success;
    }

    private static bool IsValid(SetProfessionalWeeklyAvailabilityCommand command)
    {
        if (command.ProfessionalId == Guid.Empty || command.ClinicUnitId == Guid.Empty ||
            command.Windows is null)
        {
            return false;
        }

        foreach (var group in command.Windows.GroupBy(item => item.DayOfWeek))
        {
            if (!Enum.IsDefined(group.Key))
            {
                return false;
            }

            var ordered = group.OrderBy(item => item.StartsAt).ThenBy(item => item.EndsAt).ToArray();
            for (var index = 0; index < ordered.Length; index++)
            {
                if (ordered[index].StartsAt >= ordered[index].EndsAt ||
                    index > 0 && ordered[index].StartsAt < ordered[index - 1].EndsAt)
                {
                    return false;
                }
            }
        }

        return true;
    }
}

public sealed class CreateProfessionalAvailabilityBlock(
    ICurrentPrincipalAccessor principalAccessor,
    IAppointmentStore store,
    TimeProvider timeProvider)
{
    public async Task<CreateProfessionalAvailabilityBlockResult> ExecuteAsync(
        CreateProfessionalAvailabilityBlockCommand command,
        CancellationToken cancellationToken)
    {
        if (command.ProfessionalId == Guid.Empty || command.EndsAt <= command.StartsAt ||
            !TryParseKind(command.Kind, out var kind) ||
            !string.IsNullOrWhiteSpace(command.Reason) &&
            command.Reason.Trim().Length > ProfessionalAvailabilityBlock.ReasonMaxLength)
        {
            return new(ProfessionalAvailabilityStatus.InvalidInput);
        }

        await using var transaction = await store.BeginAsync(cancellationToken);
        var access = await SchedulingAccess.ResolveAvailabilityManagerAsync(
            principalAccessor.Principal, command.ProfessionalId, transaction, timeProvider, cancellationToken);
        if (access.Status != SchedulingAccessStatus.Success)
        {
            return new(GetProfessionalAvailability.MapAccessStatus(access.Status));
        }

        var scope = access.Scope
            ?? throw new InvalidOperationException("Successful availability access must include its scope.");
        if (await transaction.HasScheduledAppointmentOverlapAsync(
                scope.TenantId,
                command.ProfessionalId,
                command.StartsAt,
                command.EndsAt,
                cancellationToken))
        {
            return new(ProfessionalAvailabilityStatus.ScheduledAppointmentConflict);
        }

        var block = new ProfessionalAvailabilityBlock(
            Guid.NewGuid(),
            scope.TenantId,
            command.ProfessionalId,
            command.StartsAt,
            command.EndsAt,
            kind,
            command.Reason,
            timeProvider.GetUtcNow());
        transaction.AddAvailabilityBlock(block);
        await transaction.CommitAsync(cancellationToken);
        return new(ProfessionalAvailabilityStatus.Success, GetProfessionalAvailability.ToBlockItem(block));
    }

    private static bool TryParseKind(string? value, out AvailabilityBlockKind kind) =>
        Enum.TryParse(value, ignoreCase: false, out kind) &&
        Enum.IsDefined(kind) &&
        kind.ToString() == value;
}

public sealed class DeleteProfessionalAvailabilityBlock(
    ICurrentPrincipalAccessor principalAccessor,
    IAppointmentStore store,
    TimeProvider timeProvider)
{
    public async Task<ProfessionalAvailabilityStatus> ExecuteAsync(
        Guid professionalId,
        Guid blockId,
        CancellationToken cancellationToken)
    {
        if (professionalId == Guid.Empty || blockId == Guid.Empty)
        {
            return ProfessionalAvailabilityStatus.InvalidInput;
        }

        await using var transaction = await store.BeginAsync(cancellationToken);
        var access = await SchedulingAccess.ResolveAvailabilityManagerAsync(
            principalAccessor.Principal, professionalId, transaction, timeProvider, cancellationToken);
        if (access.Status != SchedulingAccessStatus.Success)
        {
            return GetProfessionalAvailability.MapAccessStatus(access.Status);
        }

        var scope = access.Scope
            ?? throw new InvalidOperationException("Successful availability access must include its scope.");
        var block = await transaction.FindAvailabilityBlockAsync(
            scope.TenantId, professionalId, blockId, cancellationToken);
        if (block is null)
        {
            return ProfessionalAvailabilityStatus.BlockNotFound;
        }

        transaction.RemoveAvailabilityBlock(block);
        await transaction.CommitAsync(cancellationToken);
        return ProfessionalAvailabilityStatus.Success;
    }
}

public sealed class GetProfessionalAvailabilityBlock(
    ICurrentPrincipalAccessor principalAccessor,
    IAppointmentStore store,
    TimeProvider timeProvider)
{
    public async Task<ProfessionalAvailabilityBlockResult> ExecuteAsync(
        Guid professionalId,
        Guid blockId,
        CancellationToken cancellationToken)
    {
        if (professionalId == Guid.Empty || blockId == Guid.Empty)
        {
            return new(ProfessionalAvailabilityStatus.InvalidInput);
        }

        await using var transaction = await store.BeginAsync(cancellationToken);
        var access = await SchedulingAccess.ResolveAvailabilityManagerAsync(
            principalAccessor.Principal, professionalId, transaction, timeProvider, cancellationToken);
        if (access.Status != SchedulingAccessStatus.Success)
        {
            return new(GetProfessionalAvailability.MapAccessStatus(access.Status));
        }

        var scope = access.Scope
            ?? throw new InvalidOperationException("Successful availability access must include its scope.");
        var block = await transaction.FindAvailabilityBlockAsync(
            scope.TenantId, professionalId, blockId, cancellationToken);
        return block is null
            ? new(ProfessionalAvailabilityStatus.BlockNotFound)
            : new(ProfessionalAvailabilityStatus.Success, GetProfessionalAvailability.ToBlockItem(block));
    }
}

public sealed record SetProfessionalWeeklyAvailabilityCommand(
    Guid ProfessionalId,
    Guid ClinicUnitId,
    IReadOnlyList<ProfessionalAvailabilityWindowInput> Windows);

public sealed record ProfessionalAvailabilityWindowInput(
    DayOfWeek DayOfWeek,
    TimeOnly StartsAt,
    TimeOnly EndsAt);

public sealed record CreateProfessionalAvailabilityBlockCommand(
    Guid ProfessionalId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string? Kind,
    string? Reason);

public sealed record ProfessionalAvailabilityWindowItem(
    Guid Id,
    Guid ClinicUnitId,
    DayOfWeek DayOfWeek,
    TimeOnly StartsAt,
    TimeOnly EndsAt);

public sealed record ProfessionalAvailabilityBlockItem(
    Guid Id,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string Kind,
    string? Reason);

public sealed record ProfessionalAvailabilityData(
    Guid ProfessionalId,
    Guid ClinicUnitId,
    string TimeZoneId,
    bool IsScheduleConfigured,
    IReadOnlyList<ProfessionalAvailabilityWindowItem> WeeklyWindows,
    IReadOnlyList<ProfessionalAvailabilityBlockItem> Blocks);

public sealed record ProfessionalAvailabilityResult(
    ProfessionalAvailabilityStatus Status,
    ProfessionalAvailabilityData? Availability = null);

public sealed record CreateProfessionalAvailabilityBlockResult(
    ProfessionalAvailabilityStatus Status,
    ProfessionalAvailabilityBlockItem? Block = null);

public sealed record ProfessionalAvailabilityBlockResult(
    ProfessionalAvailabilityStatus Status,
    ProfessionalAvailabilityBlockItem? Block = null);

public enum ProfessionalAvailabilityStatus
{
    Success = 1,
    Unauthenticated = 2,
    Forbidden = 3,
    ProfessionalNotFound = 4,
    ClinicUnitUnavailable = 5,
    InvalidInput = 6,
    ScheduledAppointmentConflict = 7,
    BlockNotFound = 8,
}
