using BrothersCare.Application.Identity.Contracts;

namespace BrothersCare.Application.Tenancy.Contracts;

public interface ICommercialOnboardingSender
{
    Task SendAsync(InvitationDelivery invitation, CancellationToken cancellationToken);
}
