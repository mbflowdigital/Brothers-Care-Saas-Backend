namespace BrothersCare.Infrastructure.Email;

public sealed class MicrosoftGraphEmailSettings
{
    public MicrosoftGraphEmailSettings(
        string tenantId,
        string clientId,
        string clientSecret,
        string fromAddress,
        string fromName,
        Uri activationUrl,
        Uri commercialOnboardingUrl,
        Uri passwordRecoveryUrl)
    {
        TenantId = tenantId;
        ClientId = clientId;
        ClientSecret = clientSecret;
        FromAddress = fromAddress;
        FromName = fromName;
        ActivationUrl = activationUrl;
        CommercialOnboardingUrl = commercialOnboardingUrl;
        PasswordRecoveryUrl = passwordRecoveryUrl;
    }

    public string TenantId { get; }

    public string ClientId { get; }

    public string ClientSecret { get; }

    public string FromAddress { get; }

    public string FromName { get; }

    public Uri ActivationUrl { get; }

    public Uri CommercialOnboardingUrl { get; }

    public Uri PasswordRecoveryUrl { get; }
}
