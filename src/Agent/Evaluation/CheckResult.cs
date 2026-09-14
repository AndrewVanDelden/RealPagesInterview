using System.Text.Json.Serialization;
using Agent.Common;

namespace Agent.Evaluation;

// One check's verdict. NotMeasured is honest absence (A15): the record states no threshold,
// the message carries nothing to check, or the run did not record the value. It never
// counts as a pass in the per-check numbers and never fails a record.
[JsonConverter(typeof(SnakeCaseLowerEnumConverter<CheckResult>))]
public enum CheckResult
{
    Passed,
    Failed,
    NotMeasured,
}
