using System.Net;
using Xunit;
using static Ehr.Web.Tests.TestCases.DictationCaseFlow;

namespace Ehr.Web.Tests.TestCases.UC10;

// Test Case ID: TC-UC10-02
// Purpose / scenario: The clinician dictates an order. Dictation never places it or carries it into a field: the result
//   lists it as not carried over, for the clinician to place themselves, and staging the result for review creates no
//   order and changes no allergy. (That the order sentence is cut from the proposed plan and the rest of the plan kept
//   is checked where the cut is made, in the dictation service: test_validation.py,
//   test_an_order_sentence_is_cut_and_the_rest_of_the_plan_kept and test_an_order_or_allergy_list_change_is_never_carried.)
// Type: Functional (alternative flow)
// Requirements: FR-14
// Use case: UC 10, Structure and validate note fields (step 2 alternative: the model proposes an order or an allergy
//   change)
// Preconditions: The clinician has dictated from the dictation page for a patient on their panel, and the transcript
//   holds an instruction to order something.
// Inputs and system conditions: Stop, the result, then staging it for review. The dictation service is the test
//   fixture's stub, which reports the clinician's first word as an order instruction not carried over.
// Test data: Patient Oren Ordered, with no orders and no allergies recorded; five audio chunks.
// Steps:
//   1. Count the patient's orders and allergies.
//   2. Dictate, stop, and read the result's list of instructions not carried over.
//   3. Stage the result for review, and read the saved proposals and the patient's orders and allergies.
// Expected result: The result lists exactly one instruction not carried over, an "order", by its place in the
//   transcript only (start 0, end 5), never as text a field could carry. Staging answers 200, and the only proposals
//   saved are note fields (history and assessment). The patient still has no orders and no allergies.
// Creator: Samir Mukshe
public class TC_UC10_02_AlternativeFlow(WebApps apps) : IClassFixture<WebApps>
{
    readonly DictationSidecarStub service = apps.Dictation.Reset();

    [Fact]
    public async Task A_dictated_order_is_listed_as_not_carried_over_and_never_placed()
    {
        var flow = await BeginAsync(apps, "Oren", "Ordered");
        var orders = "SELECT count(*) FROM ehr.orders WHERE patient_id = @id";
        var allergies = "SELECT count(*) FROM ehr.allergy_intolerances WHERE patient_id = @id";
        Assert.Equal(0, await ScalarAsync<long>(apps, orders, ("id", flow.Patient)));
        Assert.Equal(0, await ScalarAsync<long>(apps, allergies, ("id", flow.Patient)));

        await DictateAsync(flow, 5);
        using var result = await ResultAsync(flow);
        var json = await JsonAsync(result);
        using var staged = await StageAsync(flow);

        var notCarried = json.GetProperty("notCarriedOver");
        Assert.Equal(1, notCarried.GetArrayLength());
        Assert.Equal("order", notCarried[0].GetProperty("reason").GetString());
        Assert.Equal((0, 5), (notCarried[0].GetProperty("start").GetInt32(), notCarried[0].GetProperty("end").GetInt32()));
        Assert.Equal(["end", "reason", "start"], notCarried[0].EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(HttpStatusCode.OK, staged.StatusCode);
        Assert.Equal("assessment,history", await ScalarAsync<string>(apps,
            "SELECT string_agg(field, ',' ORDER BY field) FROM ehr.ai_field_proposals WHERE review_id = @id", ("id", flow.Review)));
        Assert.Equal(0, await ScalarAsync<long>(apps, orders, ("id", flow.Patient)));
        Assert.Equal(0, await ScalarAsync<long>(apps, allergies, ("id", flow.Patient)));
        // The dictation service was only asked to start this dictation and about this dictation, nothing else.
        Assert.All(service.Received, request => Assert.True(
            request.PathAndQuery == "/v1/sessions" || request.PathAndQuery.StartsWith($"/v1/sessions/{flow.SessionId}/", StringComparison.Ordinal),
            request.PathAndQuery));
    }
}
