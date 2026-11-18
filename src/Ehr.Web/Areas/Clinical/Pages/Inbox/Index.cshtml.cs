using Ehr.Domain.Inbox;
using Ehr.Domain.Phi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Ehr.Web.Areas.Clinical.Pages.Inbox;

// A staff member's inbox (the expansion plan's step 3): what needs their action, oldest first, each item linking to
// its patient; and, on request, what they have done. For any staff account, clinician or admin: the area's policy. The
// database returns only the signed-in person's own items. "Mark done" is the one change, and off-site it is forwarded
// to the clinic's server like any other save. The inbox starts empty: each kind of item arrives with its own feature.
public class IndexModel(PhiReader phi, StaffInbox inbox) : PageModel
{
    public const int Limit = 50;

    // The items done, instead of those that need action.
    [BindProperty(SupportsGet = true)]
    public bool Done { get; set; }

    public InboxPage Items { get; private set; } = null!;

    // Set when "Mark done" found nothing to mark: the item was done already, in another tab perhaps.
    public bool AlreadyDone { get; private set; }

    public async Task OnGetAsync()
    {
        Response.Headers.CacheControl = "no-store";
        Items = await phi.InboxAsync(Done, Limit, HttpContext.RequestAborted);
    }

    public async Task<IActionResult> OnPostDoneAsync(Guid id)
    {
        Response.Headers.CacheControl = "no-store";
        if (await inbox.CompleteAsync(id, HttpContext.RequestAborted))
        {
            return RedirectToPage();
        }

        AlreadyDone = true;
        Items = await phi.InboxAsync(done: false, Limit, HttpContext.RequestAborted);
        return Page();
    }
}
