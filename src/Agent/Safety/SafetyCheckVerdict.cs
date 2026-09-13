namespace Agent.Safety;

// One safety check's verdict. NotApplicable is a check the record did not require: the
// constraint that gates it is absent or false, so the check never ran and its silence is not
// a pass, the same rule A15 states for the scorer. Deliberately not
// Agent.Evaluation.CheckResult, whose NotMeasured is honest absence on the measuring side (no
// threshold stated, or the value not recorded). NotApplicable is a different fact on the
// enforcing side: the value was there to check and the record said this check does not apply.
// Sharing one enum would put two meanings behind one name (HSC).
public enum SafetyCheckVerdict
{
    Passed,
    Failed,
    NotApplicable,
}
