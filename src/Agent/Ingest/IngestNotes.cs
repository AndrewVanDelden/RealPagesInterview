using System.Text.Json;
using Agent.Common;
using Agent.Domain;

namespace Agent.Ingest;

// D1: what the reader defaulted and what it did not recognize, per record. DefaultedFields
// names every decision input that was absent (or, for the timezone, unrecognized);
// UnknownMembers names every member the record types do not declare, by its path.
public sealed record IngestNotes(IReadOnlyList<string> DefaultedFields, IReadOnlyList<string> UnknownMembers)
{
    // O(m) in the number of members on the record; no member is visited twice.
    public static IngestNotes Describe(ProspectCase prospectCase)
    {
        ProspectContext context = prospectCase.ContextOrEmpty;
        CaseConstraints constraints = prospectCase.ConstraintsOrEmpty;
        var defaulted = new List<string>();

        NoteAbsent(defaulted, prospectCase.Persona is null, "persona");
        NoteAbsent(defaulted, prospectCase.LifecycleStage is null, "lifecycle_stage");
        NoteAbsent(defaulted, context.PropertyName is null, "input.property_name");
        NoteAbsent(defaulted, context.MoveDateTarget is null, "input.move_date_target");
        NoteAbsent(defaulted, context.LastInteraction is null, "input.last_interaction");
        NoteTimeZone(defaulted, context.TimeZoneId);
        NoteAbsent(defaulted, context.Language is null, "input.language");
        NoteAbsent(defaulted, context.ProfileOrEmpty.FirstName is null, "input.profile.first_name");
        NoteAbsent(defaulted, constraints.NoPiiLeak is null, "assertions.constraints.no_pii_leak");
        NoteAbsent(defaulted, constraints.IncludeOptOutInstructions is null, "assertions.constraints.include_opt_out_instructions");
        NoteAbsent(defaulted, constraints.PrimaryCta is null, "assertions.constraints.primary_cta");

        var unknown = new List<string>();
        Collect(unknown, prospectCase.UnknownMembers, string.Empty);
        Collect(unknown, prospectCase.Consent.UnknownMembers, "consent.");
        Collect(unknown, prospectCase.Input?.UnknownMembers, "input.");
        Collect(unknown, prospectCase.Input?.Profile?.UnknownMembers, "input.profile.");
        Collect(unknown, prospectCase.Assertions?.UnknownMembers, "assertions.");
        Collect(unknown, prospectCase.Assertions?.Constraints?.UnknownMembers, "assertions.constraints.");
        Collect(unknown, prospectCase.Thresholds?.UnknownMembers, "thresholds.");

        return new IngestNotes(defaulted, unknown);
    }

    private static void NoteAbsent(List<string> defaulted, bool absent, string path)
    {
        if (absent)
        {
            defaulted.Add(path);
        }
    }

    // A6: absent and unrecognized are both UTC, but an unrecognized id is worth naming.
    private static void NoteTimeZone(List<string> defaulted, string? timeZoneId)
    {
        if (timeZoneId is null)
        {
            defaulted.Add("input.timezone");
        }
        else if (!TimeZones.TryResolve(timeZoneId, out _))
        {
            defaulted.Add($"input.timezone (unrecognized '{timeZoneId}', UTC used)");
        }
    }

    private static void Collect(List<string> unknown, IDictionary<string, JsonElement>? members, string prefix)
    {
        if (members is null)
        {
            return;
        }

        foreach (string key in members.Keys)
        {
            unknown.Add(prefix + key);
        }
    }
}
