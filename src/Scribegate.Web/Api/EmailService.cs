using System.Net;
using System.Net.Mail;
using Scribegate.Core.Entities;
using Scribegate.Core.Stores;

namespace Scribegate.Web.Api;

public class EmailService(ISystemSettingStore settings, ILogger<EmailService> logger)
{
    public async Task<bool> IsEnabledAsync(CancellationToken ct = default)
    {
        var enabled = await settings.GetAsync(SystemSettingKeys.SmtpEnabled, ct);
        return enabled == "true";
    }

    public async Task SendAsync(string toEmail, string toName, string subject, string htmlBody, CancellationToken ct = default)
    {
        if (!await IsEnabledAsync(ct))
        {
            logger.LogDebug("Email not sent (SMTP disabled): {Subject} -> {To}", subject, toEmail);
            return;
        }

        try
        {
            var host = await settings.GetAsync(SystemSettingKeys.SmtpHost, ct);
            var portStr = await settings.GetAsync(SystemSettingKeys.SmtpPort, ct);
            var username = await settings.GetAsync(SystemSettingKeys.SmtpUsername, ct);
            var password = await settings.GetAsync(SystemSettingKeys.SmtpPassword, ct);
            var fromAddress = await settings.GetAsync(SystemSettingKeys.SmtpFromAddress, ct);
            var fromName = await settings.GetAsync(SystemSettingKeys.SmtpFromName, ct);
            var useSslStr = await settings.GetAsync(SystemSettingKeys.SmtpUseSsl, ct);

            if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(fromAddress))
            {
                logger.LogWarning("SMTP is enabled but host or from address is not configured");
                return;
            }

            var port = int.TryParse(portStr, out var p) ? p : 587;
            var useSsl = useSslStr != "false";

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
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send email: {Subject} -> {To}", subject, toEmail);
        }
    }
}
