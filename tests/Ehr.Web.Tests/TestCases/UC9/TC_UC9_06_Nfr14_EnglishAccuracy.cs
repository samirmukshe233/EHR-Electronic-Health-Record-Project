using Xunit;
using static Ehr.Web.Tests.TestCases.DictationCaseFlow;

namespace Ehr.Web.Tests.TestCases.UC9;

// Test Case ID: TC-UC9-06
// Purpose / scenario: English dictation is transcribed accurately across accents and background noise, and quickly
//   enough for a live transcript. This test holds the committed eval results to the case's thresholds on every change;
//   making them means running the models (TC_UC9_06_Nfr14_EnglishAccuracy.md).
// Type: Non-functional (constraint: English dictation; performance of the live transcript)
// Requirements: NFR-14, FR-07
// Use case: UC 9, Transcribe dictated audio; scope: speech recognition (MedASR) and the live transcript
// Preconditions: The dictation eval has been run with the real models and its results committed
//   (eval/results/latest.json). CI's eval gate checks that they come from this commit's code, prompt and test set.
// Inputs and system conditions: eval/results/latest.json as committed.
// Test data: The eval test set: 42 scripts and 82 synthetic English recordings (US, British, Spanish, French and Hindi
//   accents; babble, HVAC and monitor noise at 0 to 20 dB), each also fed as one-second chunks to time the live
//   transcript.
// Steps:
//   1. Read the results' test set size and whether it is complete.
//   2. Read the overall word error rate.
//   3. Read the 95th percentile of the time to answer a one-second chunk.
// Expected result: The results cover the whole test set (82 recordings, complete). The overall word error rate is at
//   most 10%. A one-second chunk is answered within 1 s at the 95th percentile.
// Creator: Samir Mukshe
public class TC_UC9_06_Nfr14_EnglishAccuracy
{
    [Fact]
    public void English_dictation_is_accurate_and_the_live_transcript_keeps_up()
    {
        var results = EvalResults();
        var dataset = results.GetProperty("dataset");
        var metrics = results.GetProperty("metrics");

        Assert.Equal(82, dataset.GetProperty("recordings").GetInt32());
        Assert.True(dataset.GetProperty("complete").GetBoolean());
        Assert.InRange(metrics.GetProperty("wer").GetProperty("overall").GetDouble(), 0, 0.10);
        Assert.InRange(metrics.GetProperty("timing").GetProperty("live_chunk_seconds").GetProperty("p95").GetDouble(), 0, 1.0);
    }
}
