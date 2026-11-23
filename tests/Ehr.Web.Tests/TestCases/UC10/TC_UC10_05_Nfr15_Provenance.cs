using System.Net;
using Xunit;
using static Ehr.Web.Tests.TestCases.DictationCaseFlow;

namespace Ehr.Web.Tests.TestCases.UC10;

// Test Case ID: TC-UC10-05
// Purpose / scenario: Every dictation result names the licensed models, llama-server build, prompt, schema and code
//   that produced it, and that record is kept with the proposals saved for review, so a note's AI text can always be
//   traced to what made it. (That the dictation service refuses model weights its manifest doesn't record is its own
//   test: TC_UC10_05_Nfr15_Provenance.md.)
// Type: Non-functional (legal: license terms)
// Requirements: NFR-15
// Use case: UC 10, Structure and validate note fields; scope: the provenance of dictation results
// Preconditions: The clinician has dictated from the dictation page for a patient on their panel and selected Stop.
// Inputs and system conditions: The result, then staging it for review. The dictation service is the test fixture's
//   stub, whose provenance has the shape of the real service's (src/Ehr.Dictation/contract).
// Test data: Patient Pia Provenance; five audio chunks.
// Steps:
//   1. Ask for the result and read its provenance.
//   2. Stage the result for review and read the provenance saved with it.
// Expected result: The result's provenance names the speech model google/medasr and the language model
//   IFM/K2-Horizon-7B-GGUF, file K2-Horizon-7B-Q4_K_M.gguf, each with a commit and a 64-character SHA-256; the
//   llama-server build b11669; hashes of the prompt, schema and structuring parameters; and a code hash beginning
//   "sha256:". The review saved by staging holds the same provenance.
// Creator: Samir Mukshe
public class TC_UC10_05_Nfr15_Provenance(WebApps apps) : IClassFixture<WebApps>
{
    readonly DictationSidecarStub service = apps.Dictation.Reset();

    [Fact]
    public async Task Every_result_names_the_models_and_code_that_made_it_and_keeps_that_with_the_review()
    {
        var flow = await BeginAsync(apps, "Pia", "Provenance");
        await DictateAsync(flow, 5);

        using var result = await ResultAsync(flow);
        var provenance = (await JsonAsync(result)).GetProperty("provenance");
        using var staged = await StageAsync(flow);

        var speech = provenance.GetProperty("speech");
        var language = provenance.GetProperty("language");
        Assert.Equal("google/medasr", speech.GetProperty("repository").GetString());
        Assert.False(string.IsNullOrEmpty(speech.GetProperty("commit").GetString()));
        Assert.Matches("^[0-9a-f]{64}$", speech.GetProperty("sha256").GetString());
        Assert.Equal("IFM/K2-Horizon-7B-GGUF", language.GetProperty("repository").GetString());
        Assert.Equal("K2-Horizon-7B-Q4_K_M.gguf", language.GetProperty("file").GetString());
        Assert.False(string.IsNullOrEmpty(language.GetProperty("commit").GetString()));
        Assert.Matches("^[0-9a-f]{64}$", language.GetProperty("sha256").GetString());
        Assert.Equal("b11669", provenance.GetProperty("llamaServer").GetString());
        Assert.All(new[] { "prompt", "schema", "structuring" }, name => Assert.False(string.IsNullOrEmpty(provenance.GetProperty(name).GetString())));
        Assert.StartsWith("sha256:", provenance.GetProperty("code").GetString());

        Assert.Equal(HttpStatusCode.OK, staged.StatusCode);
        Assert.Equal("google/medasr|K2-Horizon-7B-Q4_K_M.gguf|" + provenance.GetProperty("code").GetString(), await ScalarAsync<string>(apps,
            """
            SELECT concat_ws('|', result_json #>> '{provenance,speech,repository}', result_json #>> '{provenance,language,file}',
                result_json #>> '{provenance,code}')
            FROM ehr.ai_note_reviews WHERE id = @id
            """, ("id", flow.Review)));
        Assert.NotEmpty(service.Received);
    }
}
