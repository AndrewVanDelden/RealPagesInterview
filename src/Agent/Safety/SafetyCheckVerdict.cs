namespace Agent.Safety;

// One safety check's verdict (D38). NotApplicable is a check the record did not require:
// the constraint that gates it is absent or false, so the check never ran and its silence
// is not a pass, the same rule A15 states for the scorer.
//
// This is deliberately not Agent.Evaluation.CheckResult. That enum's third state is
// NotMeasured, which is honest absence on the measuring side: the record states no
// threshold, or the run did not record the value. NotApplicable is a different fact on the
// enforcing side: the value was there to check and the record said this check does not
// apply to it. Sharing one enum would put two meanings behind one name (HSC).
public enum SafetyCheckVerdict
{
    Passed,
    Failed,
    NotApplicable,
}
