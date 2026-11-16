namespace Ehr.Web.Forwarding;

// UC 2 (Tahseen Arnan): marks the form fields of a page that the cloud app's "Not saved" page shows again when it can't
// forward the page's POST to the on-prem app (CloudForwardingMiddleware), so what a clinician typed isn't lost, with or
// without JavaScript, even when the form's page no longer offers the form once the site is read-only. Only free text
// the person typed: never a password, a code or a form token.
[AttributeUsage(AttributeTargets.Class)]
public sealed class ShownIfNotSavedAttribute(params string[] fields) : Attribute
{
    public IReadOnlyList<string> Fields { get; } = fields;
}
