using System.Net;
using Xunit;
using static Ehr.Web.Tests.TestCases.DictationCaseFlow;

namespace Ehr.Web.Tests.TestCases.UC10;

// Test Case ID: TC-UC10-03
// Purpose / scenario: The model proposed a Plan the transcript doesn't support, and validation discarded it. The field
//   comes back empty with reason codes only: the discarded text never reaches the browser, and it isn't saved with the
//   proposals for review. (Which values validation discards is checked where it's decided, in the dictation service:
//   test_validation.py, test_a_word_the_sources_dont_have_is_discarded.)
// Type: Functional (invalid input)
// Requirements: FR-12, FR-13
// Use case: UC 10, Structure and validate note fields (step 2 alternative: a proposed value fails validation)
// Preconditions: The clinician has dictated from the dictation page for a patient on their panel and selected Stop.
// Inputs and system conditions: The result, then staging it for review. The dictation service is the test fixture's
//   stub set to "discarded": its answer carries a discarded Plan, and the discarded text again in places the API's
//   contract doesn't have (a field's note, a "debug" property), where a careless copy would pass it on.
// Test data: Patient Dex Discarded; three audio chunks; discarded text "Prescribe a drug nobody mentioned".
// Steps:
//   1. Dictate, stop, and ask for the result.
//   2. Search the result for the discarded text, and read the Plan.
//   3. Stage the result for review, and search what was saved for the discarded text.
// Expected result: The result never contains "Prescribe a drug nobody mentioned". The Plan is empty with the reason
//   "not_in_transcript" and no value; History still awaits review. Staging answers 200 and saves one proposal,
//   History; neither the proposals nor the stored result contain the discarded text.
// Creator: Samir Mukshe
public class TC_UC10_03_InvalidInput(WebApps apps) : IClassFixture<WebApps>
{
    readonly DictationSidecarStub service = apps.Dictation.Reset();

    static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_discarded_value_leaves_its_field_empty_and_is_never_shown_or_saved()
    {
        service.Sorting = StubSorting.Discarded;
        var flow = await BeginAsync(apps, "Dex", "Discarded");
        await DictateAsync(flow, 3);

        using var result = await ResultAsync(flow);
        var text = await result.Content.ReadAsStringAsync(Cancel);
        var json = await JsonAsync(result);
        using var staged = await StageAsync(flow);

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.DoesNotContain(DictationSidecarStub.DiscardedText, text);
        var plan = json.GetProperty("fields").GetProperty("plan");
        Assert.Equal(["reasons", "status"], plan.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal("empty", plan.GetProperty("status").GetString());
        Assert.Equal("not_in_transcript", plan.GetProperty("reasons")[0].GetString());
        Assert.Equal("pending", json.GetProperty("fields").GetProperty("history").GetProperty("status").GetString());

        Assert.Equal(HttpStatusCode.OK, staged.StatusCode);
        Assert.Equal("history", await ScalarAsync<string>(apps,
            "SELECT string_agg(field, ',' ORDER BY field) FROM ehr.ai_field_proposals WHERE review_id = @id", ("id", flow.Review)));
        Assert.Equal(0, await ScalarAsync<long>(apps,
            "SELECT count(*) FROM ehr.ai_field_proposals WHERE review_id = @id AND strpos(original_value, @text) > 0",
            ("id", flow.Review), ("text", DictationSidecarStub.DiscardedText)));
        Assert.Equal(0, await ScalarAsync<long>(apps,
            "SELECT count(*) FROM ehr.ai_note_reviews WHERE id = @id AND strpos(result_json::text, @text) > 0",
            ("id", flow.Review), ("text", DictationSidecarStub.DiscardedText)));
    }
}
