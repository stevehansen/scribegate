using System.Net;
using System.Net.Mail;
using Scribegate.Core.Entities;
using Scribegate.Core.Stores;

namespace Scribegate.Web.Api;

public record EmailSendResult(bool Success, string? Error);

public class EmailService(ISystemSettingStore settings, ILogger<EmailService> logger)
{
    public async Task<bool> IsEnabledAsync(CancellationToken ct = default)
    {
        var enabled = await settings.GetAsync(SystemSettingKeys.SmtpEnabled, ct);
        return enabled == "true";
    }

    public async Task SendAsync(string toEmail, string toName, string subject, string htmlBody, CancellationToken ct = default)
    {
        var result = await TrySendAsync(toEmail, toName, subject, htmlBody, ct);
        if (!result.Success)
            logger.LogError("Failed to send email: {Subject} -> {To} — {Error}", subject, toEmail, result.Error);
    }

    public async Task<EmailSendResult> TrySendAsync(string toEmail, string toName, string subject, string htmlBody, CancellationToken ct = default)
    {
        if (!await IsEnabledAsync(ct))
        {
            logger.LogDebug("Email not sent (SMTP disabled): {Subject} -> {To}", subject, toEmail);
            return new EmailSendResult(false, "SMTP is disabled.");
        }

        var host = await settings.GetAsync(SystemSettingKeys.SmtpHost, ct);
        var portStr = await settings.GetAsync(SystemSettingKeys.SmtpPort, ct);
        var username = await settings.GetAsync(SystemSettingKeys.SmtpUsername, ct);
        var password = await settings.GetAsync(SystemSettingKeys.SmtpPassword, ct);
        var fromAddress = await settings.GetAsync(SystemSettingKeys.SmtpFromAddress, ct);
        var fromName = await settings.GetAsync(SystemSettingKeys.SmtpFromName, ct);
        var useSslStr = await settings.GetAsync(SystemSettingKeys.SmtpUseSsl, ct);

        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(fromAddress))
            return new EmailSendResult(false, "smtp.host and smtp.from_address must be configured.");

        var port = int.TryParse(portStr, out var p) ? p : 587;
        var useSsl = useSslStr != "false";

        try
        {
            using var client = new SmtpClient(host, port)
            {
                EnableSsl = useSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
            };

            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
                client.Credentials = new NetworkCredential(username, password);

            var message = new MailMessage
            {
                From = new MailAddress(fromAddress, fromName ?? "Scribegate"),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true,
            };
            message.To.Add(new MailAddress(toEmail, toName));

            await client.SendMailAsync(message, ct);
            logger.LogInformation("Email sent: {Subject} -> {To}", subject, toEmail);
            return new EmailSendResult(true, null);
        }
        catch (Exception ex)
        {
            return new EmailSendResult(false, ex.Message);
        }
    }
}
