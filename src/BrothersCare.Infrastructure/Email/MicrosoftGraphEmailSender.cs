using System.Net;
using Azure.Identity;
using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Application.Tenancy.Contracts;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Users.Item.SendMail;
using Microsoft.Kiota.Abstractions;

namespace BrothersCare.Infrastructure.Email;

public sealed class MicrosoftGraphEmailSender(
    GraphServiceClient graphClient,
    MicrosoftGraphEmailSettings settings) :
    IInvitationSender,
    ICommercialOnboardingSender,
    IPasswordRecoverySender
{
    Task IInvitationSender.SendAsync(InvitationDelivery invitation, CancellationToken cancellationToken) =>
        SendInvitationAsync(invitation, settings.ActivationUrl, cancellationToken);

    Task ICommercialOnboardingSender.SendAsync(InvitationDelivery invitation, CancellationToken cancellationToken) =>
        SendInvitationAsync(invitation, settings.CommercialOnboardingUrl, cancellationToken);

    private async Task SendInvitationAsync(
        InvitationDelivery invitation,
        Uri destinationUrl,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invitation);
        var link = AddToken(destinationUrl, invitation.Token);
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
        catch (ApiException)
        {
            return false;
        }
        catch (AuthenticationFailedException)
        {
            return false;
        }
    }

    private async Task SendAsync(
        string recipient,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken)
    {
        var request = new SendMailPostRequestBody
        {
            Message = new Message
            {
                Subject = subject,
                Body = new ItemBody
                {
                    ContentType = BodyType.Html,
                    Content = htmlBody,
                },
                From = new Recipient
                {
                    EmailAddress = new EmailAddress
                    {
                        Address = settings.FromAddress,
                        Name = settings.FromName,
                    },
                },
                ToRecipients =
                [
                    new Recipient
                    {
                        EmailAddress = new EmailAddress
                        {
                            Address = recipient,
                        },
                    },
                ],
            },
            SaveToSentItems = true,
        };

        await graphClient.Users[settings.FromAddress].SendMail.PostAsync(
            request,
            cancellationToken: cancellationToken);
    }

    private static string AddToken(Uri url, string token) =>
        QueryHelpers.AddQueryString(url.ToString(), "token", token);
}
