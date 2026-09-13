using Agent.Domain;
using Agent.Ingest;
using Xunit;

namespace Agent.Tests.Ingest;

// Only task_id, consent and channel_preferences are required: every optional member a
// decision reads gets a default, and the record's diagnostics name the field. Every member
// the record types do not declare is listed by its path.
public class IngestNotesTests
{
    private const string FullLine =
        "{\"task_id\":\"full\",\"persona\":\"prospect\",\"lifecycle_stage\":\"new\"," +
        "\"consent\":{\"email_opt_in\":true,\"sms_opt_in\":true,\"voice_opt_in\":false}," +
        "\"channel_preferences\":[\"sms\"]," +
        "\"input\":{\"property_name\":\"Oak Ridge\",\"move_date_target\":\"2026-01-10\",\"last_interaction\":\"2025-12-08T15:04:00Z\",\"timezone\":\"America/Chicago\",\"language\":\"en\",\"profile\":{\"first_name\":\"Taylor\",\"city_interest\":\"Richardson, TX\",\"amenity_interest\":[\"pool\"]}}," +
        "\"assertions\":{\"required_states\":[],\"constraints\":{\"no_pii_leak\":true,\"include_opt_out_instructions\":true,\"primary_cta\":\"book_tour\"}}," +
        "\"thresholds\":{\"p95_latency_ms\":2000,\"personalization_score_min\":0.85,\"reply_classification_f1_min\":0.9,\"safety_violations_max\":0}}";

    private const string RequiredOnlyLine =
        "{\"task_id\":\"bare\",\"consent\":{\"sms_opt_in\":true},\"channel_preferences\":[\"sms\"]}";

    private static ProspectCase Parse(string line) =>
        new JsonlRecordReader().ReadAll(new StringReader(line + Environment.NewLine))[0].Value;

    [Fact]
    public void Describe_EveryDecisionInputPresent_ListsNothing()
    {
        IngestNotes notes = IngestNotes.Describe(Parse(FullLine));

        Assert.Empty(notes.DefaultedFields);
        Assert.Empty(notes.UnknownMembers);
    }

    [Fact]
    public void Describe_OnlyRequiredMembers_NamesEveryDefaultedDecisionInput()
    {
        IngestNotes notes = IngestNotes.Describe(Parse(RequiredOnlyLine));

        string[] expected =
        [
            "persona",
            "lifecycle_stage",
            "input.property_name",
            "input.move_date_target",
            "input.last_interaction",
            "input.timezone",
            "input.language",
            "input.profile.first_name",
            "input.profile.city_interest",
            "input.profile.amenity_interest",
            "assertions.constraints.no_pii_leak",
            "assertions.constraints.include_opt_out_instructions",
            "assertions.constraints.primary_cta",
            "thresholds.p95_latency_ms",
            "thresholds.personalization_score_min",
            "thresholds.reply_classification_f1_min",
            "thresholds.safety_violations_max",
        ];
        Assert.Equal(expected, notes.DefaultedFields);
        Assert.Empty(notes.UnknownMembers);
    }

    // Nested objects are optional too: an input without a profile and assertions
    // without constraints default the members those objects would have carried.
    [Fact]
    public void Describe_InputWithoutProfileAndAssertionsWithoutConstraints_ListsTheirMembersAsDefaulted()
    {
        const string line =
            "{\"task_id\":\"nested\",\"persona\":\"prospect\",\"lifecycle_stage\":\"new\"," +
            "\"consent\":{\"sms_opt_in\":true},\"channel_preferences\":[\"sms\"]," +
            "\"input\":{\"property_name\":\"Oak Ridge\",\"move_date_target\":\"2026-01-10\",\"last_interaction\":\"2025-12-08T15:04:00Z\",\"timezone\":\"America/Chicago\",\"language\":\"en\",\"unit\":\"A-204\"}," +
            "\"assertions\":{\"required_states\":[]}}";

        IngestNotes notes = IngestNotes.Describe(Parse(line));

        string[] expectedDefaulted =
        [
            "input.profile.first_name",
            "input.profile.city_interest",
            "input.profile.amenity_interest",
            "assertions.constraints.no_pii_leak",
            "assertions.constraints.include_opt_out_instructions",
            "assertions.constraints.primary_cta",
            "thresholds.p95_latency_ms",
            "thresholds.personalization_score_min",
            "thresholds.reply_classification_f1_min",
            "thresholds.safety_violations_max",
        ];
        Assert.Equal(expectedDefaulted, notes.DefaultedFields);
        Assert.Equal(["input.unit"], notes.UnknownMembers);
    }

    // A blank value is treated as absent by TemplateMessageComposer.Present, so it must be
    // named as defaulted here too - otherwise diagnostics understate what was defaulted.
    [Fact]
    public void Describe_BlankFirstNamePropertyNameAndPrimaryCta_NamesThemAsDefaulted()
    {
        string line = FullLine
            .Replace("\"first_name\":\"Taylor\"", "\"first_name\":\"  \"")
            .Replace("\"property_name\":\"Oak Ridge\"", "\"property_name\":\"\"")
            .Replace("\"primary_cta\":\"book_tour\"", "\"primary_cta\":\" \"");

        IngestNotes notes = IngestNotes.Describe(Parse(line));

        Assert.Contains("input.property_name", notes.DefaultedFields);
        Assert.Contains("input.profile.first_name", notes.DefaultedFields);
        Assert.Contains("assertions.constraints.primary_cta", notes.DefaultedFields);
    }

    // Playbook step 68: CliRunner logs DefaultedFields on every record of every run, so the
    // list says that the record's timezone was not recognized without quoting the value the
    // record wrote. Every other entry is one of this program's own schema paths, which is
    // what makes the whole list safe to log.
    [Fact]
    public void Describe_UnrecognizedTimezone_NamesItAsDefaultedWithoutTheRecordsOwnValue()
    {
        IngestNotes notes = IngestNotes.Describe(Parse(FullLine.Replace("America/Chicago", "Not/AZone")));

        string entry = Assert.Single(notes.DefaultedFields);
        Assert.StartsWith("input.timezone", entry);
        Assert.Contains("unrecognized", entry);
        Assert.DoesNotContain("Not/AZone", entry);
    }

    [Fact]
    public void Describe_UnknownMembersAtEveryDepth_ListsEachByPath()
    {
        string line = FullLine
            .Replace("\"persona\":\"prospect\"", "\"persona\":\"prospect\",\"campaign\":\"spring\"")
            .Replace("\"voice_opt_in\":false", "\"voice_opt_in\":false,\"fax_opt_in\":true")
            .Replace("\"language\":\"en\"", "\"language\":\"en\",\"unit\":\"A-204\"")
            .Replace("\"first_name\":\"Taylor\"", "\"first_name\":\"Taylor\",\"budget_max\":1700")
            .Replace("\"required_states\":[]", "\"required_states\":[],\"notes\":\"x\"")
            .Replace("\"primary_cta\":\"book_tour\"", "\"primary_cta\":\"book_tour\",\"respect_consent\":true")
            .Replace("\"safety_violations_max\":0", "\"safety_violations_max\":0,\"locale_accuracy_min\":0.95");

        IngestNotes notes = IngestNotes.Describe(Parse(line));

        string[] expected =
        [
            "campaign",
            "consent.fax_opt_in",
            "input.unit",
            "input.profile.budget_max",
            "assertions.notes",
            "assertions.constraints.respect_consent",
            "thresholds.locale_accuracy_min",
        ];
        Assert.Equal(expected, notes.UnknownMembers);
    }
}
