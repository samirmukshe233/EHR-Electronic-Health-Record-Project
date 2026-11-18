using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Ehr.Domain.Accounts;

namespace Ehr.Web.Email;

// The HTML alternative of an account email, made from its plain text, which stays the message's first part and is
// never changed (docs/brand: Email). One column on white: the lockup, the subject as the heading, the text's
// paragraphs, and one navy button, which says what the message's action does ("Confirm your account", "Reset your
// password", "Sign in") and goes to its link. It takes the place of the paragraph that is nothing but that link; when
// the text gives the link inside a sentence, the sentence keeps its links and the button follows the text.
//
// An email can load nothing from the app: mail programs drop stylesheets and block remote images. So the styles are
// inline, the colors are the tokens' light values written out (docs/brand/tokens.json: brand-navy, surface, ink-muted),
// the fonts fall back to the reader's own, and the logo travels with the message (SmtpEmailSender attaches it; the
// HTML points at it by content ID). Everything from the text is encoded. Like the text, it never carries PHI.
public static partial class EmailHtml
{
    // The content ID the HTML's logo points at.
    public const string LogoContentId = "swiftchart-lockup";

    const string Navy = "#0e1b2c";
    const string Sans = "'IBM Plex Sans', system-ui, -apple-system, 'Segoe UI', sans-serif";
    const string Display = "'Sora', system-ui, -apple-system, 'Segoe UI', sans-serif";

    public static string From(string subject, string text, EmailAction action)
    {
        var html = new StringBuilder();
        html.Append("<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n")
            .Append("<meta name=\"color-scheme\" content=\"light only\">\n")
            .Append($"<title>{Encode(subject)}</title>\n</head>\n")
            .Append($"<body style=\"margin:0;padding:0;background:#ffffff;color:{Navy};font-family:{Sans};font-size:18px;line-height:28px\">\n")
            .Append("<div style=\"max-width:40rem;margin:0 auto;padding:32px 20px\">\n")
            .Append($"<img src=\"cid:{LogoContentId}\" alt=\"SwiftChart\" width=\"140\" style=\"display:block;width:140px;height:auto;margin:0 0 32px;border:0\">\n")
            .Append($"<h1 style=\"margin:0 0 16px;font-family:{Display};font-size:22px;line-height:28px;font-weight:600\">{Encode(subject)}</h1>\n");

        // Only a web address is ever a link.
        if (!action.Link.StartsWith("https://", StringComparison.Ordinal) && !action.Link.StartsWith("http://", StringComparison.Ordinal))
        {
            throw new ArgumentException("An email's action must link to a web address.", nameof(action));
        }

        var button = $"<p style=\"margin:24px 0\"><a href=\"{Encode(action.Link)}\" style=\"display:inline-block;background:{Navy};color:#ffffff;"
            + $"font-family:{Sans};font-size:16px;line-height:44px;font-weight:600;padding:0 24px;border-radius:4px;text-decoration:none\">"
            + $"{Encode(action.Label)}</a></p>\n";
        var placed = false;
        foreach (var paragraph in Blank().Split(text.ReplaceLineEndings("\n").Trim()).Select(part => part.Trim()).Where(part => part.Length > 0))
        {
            // One button per message, where the text gives the action's link on a line of its own.
            if (!placed && paragraph == action.Link)
            {
                placed = true;
                html.Append(button);
            }
            else
            {
                html.Append($"<p style=\"margin:0 0 16px\">{Linked(paragraph)}</p>\n");
            }
        }

        if (!placed)
        {
            html.Append(button);
        }

        return html.Append("</div>\n</body>\n</html>\n").ToString();
    }

    // The paragraph encoded, each web address in it a link, and its line breaks kept.
    static string Linked(string paragraph)
    {
        var html = new StringBuilder();
        var at = 0;
        foreach (Match match in Link().Matches(paragraph))
        {
            // A sentence's own punctuation after an address isn't part of it.
            var address = match.Value.TrimEnd('.', ',', ';', ':', ')', '!', '?');
            html.Append(Encode(paragraph[at..match.Index]))
                .Append($"<a href=\"{Encode(address)}\" style=\"color:#0b6e5c\">{Encode(address)}</a>");
            at = match.Index + address.Length;
        }

        return html.Append(Encode(paragraph[at..])).Replace("\n", "<br>\n").ToString();
    }

    static string Encode(string value) => WebUtility.HtmlEncode(value);

    [GeneratedRegex(@"\n[ \t]*\n")]
    private static partial Regex Blank();

    [GeneratedRegex(@"https?://[^\s<>""]+")]
    private static partial Regex Link();
}
