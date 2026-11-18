using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using Ehr.Domain.Accounts;
using Microsoft.Extensions.Options;

namespace Ehr.Web.Email;

// Plain SMTP, unauthenticated, to the host in Ehr:Smtp: enough for the stack's mail catcher and a clinic's relay on
// the same network. The messages carry an address and a link, never PHI.
public sealed class SmtpEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var client = new SmtpClient(settings.Host, settings.Port);
        using var message = Compose(settings.From, to, subject, body);
        await client.SendMailAsync(message, cancellationToken);
    }

    // The message as it is sent: the plain text exactly as it was given, and beside it the HTML made from it
    // (EmailHtml), for mail programs that show it. The HTML's logo is attached to the message itself, since a mail
    // program won't fetch one, and nothing in the message points at another host.
    public static MailMessage Compose(string from, string to, string subject, string body)
    {
        var message = new MailMessage(from, to, subject, body);
        var html = AlternateView.CreateAlternateViewFromString(EmailHtml.From(subject, body), Encoding.UTF8, MediaTypeNames.Text.Html);
        html.LinkedResources.Add(new LinkedResource(Logo(), "image/png")
        {
            ContentId = EmailHtml.LogoContentId,
            TransferEncoding = TransferEncoding.Base64,
        });
        message.AlternateViews.Add(html);
        return message;
    }

    // The color lockup, for the email's white ground, built into the assembly from Ehr.Design's copy (Ehr.Web.csproj).
    static Stream Logo() =>
        typeof(SmtpEmailSender).Assembly.GetManifestResourceStream("swiftchart-lockup.png")
        ?? throw new InvalidOperationException("The email logo isn't built into the app.");
}
