using System.Collections.Concurrent;
using Ehr.Domain.Accounts;

namespace Ehr.Web.Tests;

// Keeps every email the apps send, so a test can follow the link in it.
public sealed class RecordingEmailSender : IEmailSender
{
    public ConcurrentQueue<(string To, string Subject, string Body)> Sent { get; } = new();

    // What each message's button says and where it goes, by the message's body.
    public ConcurrentDictionary<string, EmailAction> Actions { get; } = new();

    public Task SendAsync(string to, string subject, string body, EmailAction action, CancellationToken cancellationToken)
    {
        Sent.Enqueue((to, subject, body));
        Actions[body] = action;
        return Task.CompletedTask;
    }
}
