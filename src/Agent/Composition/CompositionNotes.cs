namespace Agent.Composition;

// D24: how the message was produced, for the diagnostics file, the way ScheduleNotes carries
// how send_at was reached (D22). Composer is the implementation whose text was returned, one
// of ComposerNames; Attempts is how many compose calls the compose-validate loop made to get
// it. Degradation needs no flag of its own: the run states the composer it asked for, so a
// record that names the template composer on an openai run is one the fallback answered.
public sealed record CompositionNotes(string Composer, int Attempts);
