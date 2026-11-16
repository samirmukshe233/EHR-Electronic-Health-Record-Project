using Ehr.Data.Patients;
using Ehr.Domain.Results;
using Xunit;

namespace Ehr.Web.Tests;

// Unit tests of ResultExplanation, the plain-language wording of a test result for the portal (FR-16; Simar Rekhi). No
// database: each builds one result and checks the words. Test cases TC-FR16-01 to TC-FR16-05 in the SE document.
public class ResultExplanationTests
{
    [Theory]
    [InlineData(6.9, 4.0, 5.6, ResultFlag.High)]
    [InlineData(3.9, 4.0, 5.6, ResultFlag.Low)]
    [InlineData(5.0, 4.0, 5.6, ResultFlag.Normal)]
    // The ends of the range are normal.
    [InlineData(4.0, 4.0, 5.6, ResultFlag.Normal)]
    [InlineData(5.6, 4.0, 5.6, ResultFlag.Normal)]
    public void A_value_is_compared_with_both_ends_of_its_range(double value, double low, double high, ResultFlag expected) =>
        Assert.Equal(expected, ResultExplanation.Flag(Result((decimal)value, (decimal)low, (decimal)high)));

    [Fact]
    public void A_range_with_one_end_reads_as_people_say_it()
    {
        var ldl = Result(130m, null, 100m, "mg/dL");

        Assert.Equal("below 100 mg/dL", ResultExplanation.Range(ldl));
        Assert.Equal("Your result is above the normal range (below 100 mg/dL).", ResultExplanation.Meaning(ldl));
        Assert.Equal("above 40 mg/dL", ResultExplanation.Range(Result(55m, 40m, null, "mg/dL")));
    }

    [Fact]
    public void Numbers_drop_the_zeros_the_database_keeps()
    {
        var a1c = Result(6.9000m, 4.0000m, 5.6000m, "%");

        Assert.Equal("6.9 %", ResultExplanation.Value(a1c));
        Assert.Equal("4 to 5.6 %", ResultExplanation.Range(a1c));
    }

    [Fact]
    public void Without_a_number_or_a_range_the_patient_is_sent_to_their_care_team()
    {
        Assert.Equal(ResultFlag.None, ResultExplanation.Flag(Result(null, 4m, 5.6m)));
        Assert.Contains("no number to compare", ResultExplanation.Meaning(Result(null, 4m, 5.6m)));
        Assert.Equal(ResultFlag.None, ResultExplanation.Flag(Result(12m, null, null)));
        Assert.Contains("no normal range to compare with", ResultExplanation.Meaning(Result(12m, null, null)));
    }

    [Fact]
    public void Known_tests_say_what_they_measure_and_unknown_ones_say_nothing()
    {
        Assert.Equal("This test measures your average blood sugar over the past 2 to 3 months.",
            ResultExplanation.WhatItMeasures(Result(6.9m, 4m, 5.6m, code: "4548-4")));
        Assert.Null(ResultExplanation.WhatItMeasures(Result(1m, 0m, 2m, code: "99999-9")));
        Assert.Null(ResultExplanation.WhatItMeasures(Result(1m, 0m, 2m, code: null)));
    }

    static Observation Result(decimal? value, decimal? low, decimal? high, string? unit = null, string? code = "4548-4") => new()
    {
        Id = Guid.CreateVersion7(),
        PatientId = Guid.CreateVersion7(),
        Code = code,
        Display = "Test",
        Status = ObservationStatus.Final,
        Value = value,
        Unit = unit,
        ReferenceLow = low,
        ReferenceHigh = high,
        EffectiveAt = DateTimeOffset.UtcNow,
        IssuedAt = DateTimeOffset.UtcNow,
    };
}
