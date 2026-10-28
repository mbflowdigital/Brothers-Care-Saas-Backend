using System.Net;
using System.Net.Mail;
using BrothersCare.Application.Identity.Contracts;
using Microsoft.AspNetCore.WebUtilities;

namespace BrothersCare.Infrastructure.Email;

public sealed class SmtpEmailSender(SmtpEmailSettings settings) : IInvitationSender, IPasswordRecoverySender
{
    public async Task SendAsync(InvitationDelivery invitation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invitation);
        var link = AddToken(settings.ActivationUrl, invitation.Token);
        var name = WebUtility.HtmlEncode(invitation.DisplayName);
        var tenant = WebUtility.HtmlEncode(invitation.TenantName);
        var body = $"""
            <p>Olá, {name}.</p>
            <p>Você foi convidado(a) para acessar a empresa <strong>{tenant}</strong> no Brothers Care.</p>
            <p><a href="{WebUtility.HtmlEncode(link)}">Definir senha e aceitar convite</a></p>
            <p>Este link expira em {invitation.ExpiresAt:dd/MM/yyyy HH:mm} UTC.</p>
            <p>Se você não reconhece este convite, ignore esta mensagem.</p>
            """;
        await SendAsync(invitation.Email, "Convite para o Brothers Care", body, cancellationToken);
    }

    public async Task<bool> TrySendAsync(PasswordRecoveryDelivery delivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        try
        {
            var link = AddToken(settings.PasswordRecoveryUrl, delivery.Token);
            var body = $"""
                <p>Recebemos uma solicitação para redefinir a senha da sua conta Brothers Care.</p>
                <p><a href="{WebUtility.HtmlEncode(link)}">Redefinir senha</a></p>
                <p>Este link expira em {delivery.ExpiresAt:dd/MM/yyyy HH:mm} UTC.</p>
                <p>Se você não solicitou a redefinição, ignore esta mensagem.</p>
                """;
            await SendAsync(delivery.Email, "Redefinição de senha do Brothers Care", body, cancellationToken);
            return true;
        }
        catch (SmtpException)
        {
            return false;
        }
    }

    private async Task SendAsync(string recipient, string subject, string htmlBody, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var message = new MailMessage
        {
            From = new MailAddress(settings.FromAddress, settings.FromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true,
        };
        message.To.Add(new MailAddress(recipient));

        using var client = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = true,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(settings.UserName, settings.Password),
        };
        await client.SendMailAsync(message, cancellationToken);
    }

    private static string AddToken(Uri url, string token) =>
        QueryHelpers.AddQueryString(url.ToString(), "token", token);
}
