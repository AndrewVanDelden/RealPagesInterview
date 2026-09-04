namespace Agent.Common;

public static class ExceptionFormatting
{
    // A bare ex.Message alone ("Value cannot be null. (Parameter 'key')") does not say
    // what went wrong - the exception type is what makes it actionable in a log line.
    public static string ToDiagnosticString(this Exception ex) => $"{ex.GetType().Name}: {ex.Message}";
}
