namespace BrothersCare.Infrastructure.Email;

public sealed class SmtpEmailSettings
{
    public SmtpEmailSettings(
        string host,
        int port,
        string userName,
        string password,
        string fromAddress,
        string fromName,
        Uri activationUrl,
        Uri commercialOnboardingUrl,
        Uri passwordRecoveryUrl)
    {
        Host = host;
        Port = port;
        UserName = userName;
        Password = password;
        FromAddress = fromAddress;
        FromName = fromName;
        ActivationUrl = activationUrl;
        CommercialOnboardingUrl = commercialOnboardingUrl;
        PasswordRecoveryUrl = passwordRecoveryUrl;
    }

    public string Host { get; }
    public int Port { get; }
    public string UserName { get; }
    public string Password { get; }
    public string FromAddress { get; }
    public string FromName { get; }
    public Uri ActivationUrl { get; }
    public Uri CommercialOnboardingUrl { get; }
    public Uri PasswordRecoveryUrl { get; }
}
