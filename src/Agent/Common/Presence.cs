namespace Agent.Common;

// The one place "absent" is defined for a free-text input field: null and blank
// (whitespace-only) both count. Composition, ingest diagnostics, and any future consumer
// share this so a blank value is never treated as present by one and absent by another.
public static class Presence
{
    public static bool IsAbsent(string? value) => string.IsNullOrWhiteSpace(value);
}
