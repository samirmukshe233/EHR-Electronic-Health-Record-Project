using System.Net.Mail;
using Ehr.Web.Email;
using Xunit;

namespace Ehr.Web.Tests;

// An account email keeps its plain text exactly, and gains an HTML alternative made from it (docs/brand: Email): one
// column, the lockup, one navy button. Nothing in it is loaded from anywhere: the logo travels with the message.
public class EmailHtmlTests
{
    const string Subject = "Confirm your patient portal account";
    const string Link = "https://portal.example.com/portal/confirm?user=1&token=a%2Bb";

    const string Text =
        $"""
        Open this link to confirm your email address and finish setting up your patient portal account:

        {Link}

        If you didn't register, you can ignore this message.
        """;

    [Fact]
    public void A_bare_link_becomes_the_one_button_named_for_the_action()
    {
        var html = EmailHtml.From(Subject, Text);

        Assert.Contains($"<h1 style=\"margin:0 0 16px;", html);
        Assert.Contains($">{Subject}</h1>", html);
        Assert.Contains("<p style=\"margin:0 0 16px\">Open this link to confirm your email address and finish setting up your patient portal account:</p>", html);
        // The link's "&" is encoded in the attribute; the button is navy with white text and says what it does.
        Assert.Contains("<a href=\"https://portal.example.com/portal/confirm?user=1&amp;token=a%2Bb\" style=\"display:inline-block;background:#0e1b2c;color:#ffffff;", html);
        Assert.Contains($">{Subject}</a></p>", html);
        Assert.Single(html.Split("display:inline-block").Skip(1));
        Assert.Contains("If you didn&#39;t register, you can ignore this message.", html);
    }

    [Fact]
    public void A_link_inside_a_sentence_stays_a_link_without_the_sentences_punctuation()
    {
        var html = EmailHtml.From("Your patient portal account",
            "If it was you, sign in at https://portal.example.com/portal/sign-in or reset your password at https://portal.example.com/portal/forgot. If it wasn't, you can ignore this message.");

        Assert.Contains("sign in at <a href=\"https://portal.example.com/portal/sign-in\" style=\"color:#0b6e5c\">https://portal.example.com/portal/sign-in</a> or reset", html);
        Assert.Contains("<a href=\"https://portal.example.com/portal/forgot\" style=\"color:#0b6e5c\">https://portal.example.com/portal/forgot</a>. If it wasn&#39;t", html);
        Assert.DoesNotContain("display:inline-block", html);
    }

    [Fact]
    public void Everything_from_the_text_is_encoded_and_nothing_is_loaded_from_another_host()
    {
        var html = EmailHtml.From("A <b>subject</b>", "<script>alert(1)</script>\n\njavascript:alert(1)\n\n" + Text);

        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("<b>", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        // Only a web address becomes a link.
        Assert.DoesNotContain("href=\"javascript:", html);
        // The one image is the logo attached to the message; no stylesheet, script or remote image.
        var image = Assert.Single(html.Split("<img ").Skip(1));
        Assert.StartsWith($"src=\"cid:{EmailHtml.LogoContentId}\" alt=\"SwiftChart\"", image);
        Assert.DoesNotContain("<link", html);
        Assert.DoesNotContain("url(", html);
        Assert.DoesNotContain("src=\"http", html);
    }

    [Fact]
    public void The_message_sent_keeps_the_plain_text_and_carries_the_html_with_its_logo()
    {
        using var message = SmtpEmailSender.Compose("no-reply@example.com", "patient@example.com", Subject, Text);

        // The plain text, exactly as it was given.
        Assert.Equal(Text, message.Body);
        Assert.False(message.IsBodyHtml);
        Assert.Equal(Subject, message.Subject);

        var html = Assert.Single(message.AlternateViews);
        Assert.Equal("text/html", html.ContentType.MediaType);
        using var reader = new StreamReader(html.ContentStream);
        Assert.Equal(EmailHtml.From(Subject, Text), reader.ReadToEnd());

        var logo = Assert.Single(html.LinkedResources);
        Assert.Equal(EmailHtml.LogoContentId, logo.ContentId);
        Assert.Equal("image/png", logo.ContentType.MediaType);
        // A PNG, as the file starts.
        var start = new byte[4];
        logo.ContentStream.ReadExactly(start);
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], start);
        Assert.Empty(message.Attachments);
    }
}
