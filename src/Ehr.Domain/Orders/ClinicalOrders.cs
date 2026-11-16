using Ehr.Data;
using Ehr.Data.Orders;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ehr.Domain.Orders;

// UC 1 Place an order (Tahseen Arnan). Called inside the page's database transaction (DatabaseTransactionFilter). The
// catalog isn't PHI, so it is read here; the patient and their orders are read through PhiReader. Who may sign is the
// order page's rule and the database's (a clinician, as themselves, for a patient on their panel: db/policies/30-rls.sql),
// and the database also sets the time, records the write, and refuses a duplicate without a reason (60-orders.sql).
public sealed class ClinicalOrders(EhrDbContext db)
{
    // The savepoint the insert runs in, so the request's transaction outlives a refused duplicate.
    const string Savepoint = "sign_order";

    public async Task<IReadOnlyList<OrderCatalogItem>> CatalogAsync(string type, CancellationToken cancellationToken) =>
        await db.OrderCatalogItems.AsNoTracking().Where(i => i.Type == type).OrderBy(i => i.Display).ToListAsync(cancellationToken);

    // What is wrong with an order before it goes to the database, by field: "Item", or a detail's name. The item is the
    // catalog's, as the clinician picked or typed it, in any case.
    public async Task<(OrderCatalogItem? Item, Dictionary<string, string> Errors)> CheckAsync(
        string type, string? item, IReadOnlyDictionary<string, string?> details, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>();
        var display = item?.Trim().ToLowerInvariant();
        var found = string.IsNullOrEmpty(display)
            ? null
            : await db.OrderCatalogItems.AsNoTracking().SingleOrDefaultAsync(i => i.Type == type && i.Display.ToLower() == display, cancellationToken);
        if (found is null)
        {
            errors["Item"] = string.IsNullOrWhiteSpace(item) ? "Choose an item." : "Choose an item from the list.";
        }

        foreach (var field in OrderForms.Fields[type])
        {
            var value = details.GetValueOrDefault(field.Name);
            if (field.Required && string.IsNullOrWhiteSpace(value))
            {
                errors[field.Name] = $"Enter the {field.Label.ToLowerInvariant()}.";
            }
            else if (value?.Trim().Length > OrderForms.MaximumLength)
            {
                errors[field.Name] = $"Shorten this to {OrderForms.MaximumLength} characters or fewer.";
            }
        }

        return (found, errors);
    }

    // Signs the order: the database sets the signer and the time. A blank reason is none. Null when the database refused
    // it as a duplicate without a reason, one signed at the same moment as another; the order page then warns as for any
    // duplicate. Only in the page's own transaction, READ COMMITTED, which the signing trigger requires.
    public async Task<Order?> SignAsync(Guid patient, OrderCatalogItem item, string details, string? duplicateReason, CancellationToken cancellationToken)
    {
        var order = new Order
        {
            Id = Guid.CreateVersion7(),
            PatientId = patient,
            Type = item.Type,
            CatalogCode = item.Code,
            Details = details,
            Status = OrderStatus.Active,
            DuplicateReason = string.IsNullOrWhiteSpace(duplicateReason) ? null : duplicateReason.Trim(),
        };
        var transaction = db.Database.CurrentTransaction!;
        await transaction.CreateSavepointAsync(Savepoint, cancellationToken);
        db.AddRange(order);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry((object)order).State = EntityState.Detached;
            await transaction.RollbackToSavepointAsync(Savepoint, cancellationToken);
            return null;
        }

        await transaction.ReleaseSavepointAsync(Savepoint, cancellationToken);
        return order;
    }
}
