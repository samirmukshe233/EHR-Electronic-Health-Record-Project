namespace Ehr.Domain.Accounts;

// Sends the portal's emails: confirmation links and password reset links. The host decides how (Program.cs): logged
// in development, SMTP to the stack's mail catcher otherwise. A message never carries PHI: an address and a link.
//
// The body is the message's plain text, complete on its own. The action names the one thing the message asks the
// person to do and the link that does it, for the button in the message's HTML version (Ehr.Web/Email/EmailHtml).
public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, EmailAction action, CancellationToken cancellationToken);
}

// What an email's button says and where it goes: "Confirm your account", "Reset your password", "Sign in". The link is
// one the plain text already gives.
public sealed record EmailAction(string Label, string Link);
