namespace Agent.Composition;

// D24: how the message was produced, for the diagnostics file, the way ScheduleNotes carries
// how send_at was reached (D22). Composer is the implementation whose text was returned, one
// of ComposerNames; Attempts is how many compose calls the compose-validate loop made to get
// it. Degradation needs no flag of its own: the run states the composer it asked for, so a
// record that names the template composer on an openai run is one the fallback answered.
// LocaleApplied is A13's locale_not_applied as a value: it says the composer could serve the
// record's stated language. The template composer sets it from its own list of template sets,
// so there it is a check that fails; the model composer passes the tag through and can serve
// any language (D26), so there it states that capability, and the evaluator's own language
// check is what measures the text either way.
// NetworkRetries is how many transport retries the call underneath spent (D28, playbook
// step 49), and is null for a composer that makes no network call at all.
public sealed record CompositionNotes(string Composer, int Attempts, bool LocaleApplied, int? NetworkRetries = null)
{
    // A composer building its own notes never knows the real attempt count: that is the
    // compose-validate loop's fact, stamped on afterward by ValidatingMessageComposer.WithAttempts,
    // and unconditionally overwrites whatever is passed here. Attempts: 1 is a placeholder no
    // caller ever observes, spelled once instead of once per composer.
    public static CompositionNotes ForComposer(string composer, bool localeApplied, int? networkRetries = null) =>
        new(composer, Attempts: 1, localeApplied, networkRetries);
}
