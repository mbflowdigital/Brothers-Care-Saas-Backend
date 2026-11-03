using BrothersCare.Domain.Care;
using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.Application.Tenancy.Contracts;

public interface IClinicUnitStore
{
    Task<IClinicUnitTransaction> BeginAsync(CancellationToken cancellationToken);
}

public interface IClinicUnitTransaction : IAsyncDisposable
{
    Task<UserSession?> FindSessionAsync(Guid sessionId, CancellationToken cancellationToken);

    Task<bool> HasActiveCredentialAsync(Guid userId, CancellationToken cancellationToken);

    Task<bool> HasActiveClinicAdministratorAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);

    Task<ClinicUnitPage> ListAsync(Guid tenantId, string? search, int page, int pageSize, CancellationToken cancellationToken);

    Task<ClinicUnitDetails?> FindAsync(Guid tenantId, Guid clinicUnitId, CancellationToken cancellationToken);

    Task<ClinicUnit?> FindTrackedAsync(Guid tenantId, Guid clinicUnitId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Appointment>> FindFutureScheduledAppointmentsAsync(
        Guid tenantId,
        Guid clinicUnitId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<bool> NameExistsAsync(Guid tenantId, string name, Guid? exceptClinicUnitId, CancellationToken cancellationToken);

    void Add(ClinicUnit clinicUnit);

    Task CommitAsync(CancellationToken cancellationToken);
}
