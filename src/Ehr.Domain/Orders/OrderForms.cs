using System.Text.Json;
using Ehr.Data.Orders;

namespace Ehr.Domain.Orders;

// UC 1 Place an order (Tahseen Arnan): what the order form asks for each type, beyond the item. The fields, which of
// them are required, how they are kept (Order.Details, a JSON object of the ones filled in) and how the chart shows them.
public static class OrderForms
{
    public const int MaximumLength = 200;

    public const int MaximumReasonLength = 500;

    public static readonly IReadOnlyDictionary<string, string> TypeNames = new Dictionary<string, string>
    {
        [OrderType.Lab] = "Lab",
        [OrderType.Imaging] = "Imaging",
        [OrderType.Medication] = "Medication",
        [OrderType.Referral] = "Referral",
    };

    public static readonly IReadOnlyDictionary<string, OrderField[]> Fields = new Dictionary<string, OrderField[]>
    {
        [OrderType.Lab] = [new("indication", "Reason for the test", Required: true), new("notes", "Notes for the lab", Required: false)],
        [OrderType.Imaging] = [new("indication", "Reason for the exam", Required: true), new("notes", "Notes for imaging", Required: false)],
        [OrderType.Medication] =
        [
            new("dose", "Dose", Required: true), new("route", "Route", Required: true), new("frequency", "Frequency", Required: true),
            new("duration", "Duration", Required: false),
        ],
        [OrderType.Referral] = [new("reason", "Reason for referral", Required: true), new("notes", "Notes for the specialist", Required: false)],
    };

    // The type's details as they are kept: the fields filled in, trimmed. Anything else posted is dropped.
    public static string Details(string type, IReadOnlyDictionary<string, string?> values) =>
        JsonSerializer.Serialize(Fields[type]
            .Where(field => !string.IsNullOrWhiteSpace(values.GetValueOrDefault(field.Name)))
            .ToDictionary(field => field.Name, field => values[field.Name]!.Trim()));

    // The details as they were kept, by field, to fill the form again when an order is changed.
    public static Dictionary<string, string?> Values(string details) =>
        (JsonSerializer.Deserialize<Dictionary<string, string>>(details) ?? []).ToDictionary(pair => pair.Key, pair => (string?)pair.Value);

    // The details as the chart lists them, in the form's order: "Dose: 500 mg; Route: by mouth".
    public static string Summary(string type, string details)
    {
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(details) ?? [];
        return string.Join("; ", Fields[type].Where(field => values.ContainsKey(field.Name)).Select(field => $"{field.Label}: {values[field.Name]}"));
    }
}

public sealed record OrderField(string Name, string Label, bool Required);

// An active order as the chart and the order form's duplicate warning show it: only the fields they show.
public sealed record ActiveOrder(Guid Id, string Type, string Display, string Details, DateTimeOffset SignedAt);

// A discontinued order as its list shows it: what it was, who discontinued it, when and why, and whether a changed
// order took its place.
public sealed record DiscontinuedOrder(
    Guid Id, string Type, string Display, string Details, DateTimeOffset SignedAt, DateTimeOffset DiscontinuedAt, string DiscontinuedBy, string Reason,
    bool Replaced);
