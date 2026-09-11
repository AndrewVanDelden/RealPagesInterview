namespace Agent.Composition;

// How the message was produced, for the diagnostics (playbook step 57), as ScheduleNotes says
// how send_at was reached. Composer is the implementation whose text was returned, one of
// ComposerNames; Attempts is the compose calls the compose-validate loop made. A record naming
// the template composer on an openai run is one the fallback answered, so degradation needs no
// flag. LocaleApplied is A13's locale_not_applied as a value: a check against the template sets
// for the template composer, a stated capability for the model composer, which passes any
// language through; the evaluator's language check measures the text either way. A record with
// no message has none of these, so this is null there; its spend rides on ComposeOutcome.
public sealed record CompositionNotes(
    string Composer,
    int Attempts,
    bool LocaleApplied)
{
    // A composer building its own notes never knows the real attempt count: that is the
    // compose-validate loop's fact, stamped on afterward by ValidatingMessageComposer.WithAttempts,
    // and unconditionally overwrites whatever is passed here. Attempts: 1 is a placeholder no
    // caller ever observes, spelled once instead of once per composer.
    public static CompositionNotes ForComposer(string composer, bool localeApplied) =>
        new(composer, Attempts: 1, localeApplied);
}
