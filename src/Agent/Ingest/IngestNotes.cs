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
        ProspectProfile profile = context.ProfileOrEmpty;
        CaseConstraints constraints = prospectCase.ConstraintsOrEmpty;
        CaseThresholds thresholds = prospectCase.ThresholdsOrEmpty;
        var defaulted = new List<string>();

        NoteAbsent(defaulted, prospectCase.Persona is null, "persona");
        NoteAbsent(defaulted, prospectCase.LifecycleStage is null, "lifecycle_stage");
        NoteAbsent(defaulted, Presence.IsAbsent(context.PropertyName), "input.property_name");
        NoteAbsent(defaulted, context.MoveDateTarget is null, "input.move_date_target");
        NoteAbsent(defaulted, context.LastInteraction is null, "input.last_interaction");
        NoteTimeZone(defaulted, context.TimeZoneId);
        NoteAbsent(defaulted, context.Language is null, "input.language");
        NoteAbsent(defaulted, Presence.IsAbsent(profile.FirstName), "input.profile.first_name");
        NoteAbsent(defaulted, profile.City.Length == 0, "input.profile.city_interest");
        NoteAbsent(defaulted, profile.Amenities.Count == 0, "input.profile.amenity_interest");
        NoteAbsent(defaulted, constraints.NoPiiLeak is null, "assertions.constraints.no_pii_leak");
        NoteAbsent(defaulted, constraints.IncludeOptOutInstructions is null, "assertions.constraints.include_opt_out_instructions");
        NoteAbsent(defaulted, Presence.IsAbsent(constraints.PrimaryCta), "assertions.constraints.primary_cta");
        NoteAbsent(defaulted, thresholds.P95LatencyMs is null, "thresholds.p95_latency_ms");
        NoteAbsent(defaulted, thresholds.PersonalizationScoreMin is null, "thresholds.personalization_score_min");
        NoteAbsent(defaulted, thresholds.ReplyClassificationF1Min is null, "thresholds.reply_classification_f1_min");
        NoteAbsent(defaulted, thresholds.SafetyViolationsMax is null, "thresholds.safety_violations_max");

        var unknown = new List<string>();
        Collect(unknown, prospectCase, string.Empty);
        Collect(unknown, prospectCase.Consent, "consent.");
        Collect(unknown, prospectCase.Input, "input.");
        Collect(unknown, prospectCase.Input?.Profile, "input.profile.");
        Collect(unknown, prospectCase.Assertions, "assertions.");
        Collect(unknown, prospectCase.Assertions?.Constraints, "assertions.constraints.");
        Collect(unknown, prospectCase.Thresholds, "thresholds.");

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

    private static void Collect(List<string> unknown, HasUnknownMembers? source, string prefix)
    {
        if (source?.UnknownMembers is not { } members)
        {
            return;
        }

        foreach (string key in members.Keys)
        {
            unknown.Add(prefix + key);
        }
    }
}
