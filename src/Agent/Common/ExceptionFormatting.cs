using System.ClientModel;
using System.Globalization;
using System.Text.Json;

namespace Agent.Common;

public static class ExceptionFormatting
{
    // A bare ex.Message alone ("Value cannot be null. (Parameter 'key')") does not say
    // what went wrong - the exception type is what makes it actionable in a log line.
    // Only for an exception this program or the framework authored the message of: an
    // unopenable --log-file, an unknown --composer, a missing key. Anything whose message
    // could have been written by the vendor, the model, or a prospect record goes through
    // ToRedactedDiagnosticString instead.
    public static string ToDiagnosticString(this Exception ex) => $"{ex.GetType().Name}: {ex.Message}";

    // Playbook step 68: no vendor error body, no fragment of a model response, and no text a
    // prospect record authored reaches a log line. Two exception types in this program carry
    // exactly that in their own Message: ClientResultException, whose Message is the vendor's
    // raw error response body, and JsonException, whose Message names the offending character
    // of whatever was being parsed - a model's completion or a prospect record's line.
    //
    // What is reported instead is a category and a bounded descriptor no content can appear
    // in: the exception type, an HTTP status, a line number, a byte offset. JsonException.Path
    // goes with the Message rather than staying: a member a record or a model authored is
    // captured as extension data rather than declared, and the path to one is that member's
    // own name, which is free text (proved by the rendered-log tests in Agent.Cli.Tests).
    // Every other type reports its name alone, because an Exception variable cannot promise
    // who wrote its Message; the caller's own log message says which operation failed.
    //
    // System.ClientModel is referenced here rather than in Agent.Composition alone because
    // the JsonException arm is needed by the two readers and by the expected-outcome
    // converter as well, and one rule in one place is what makes it checkable.
    public static string ToRedactedDiagnosticString(this Exception ex) => ex switch
    {
        ClientResultException vendorFailure => $"{ex.GetType().Name}: HTTP status {vendorFailure.Status}",
        JsonException parseFailure =>
            $"{ex.GetType().Name}: line {Position(parseFailure.LineNumber)}, byte position {Position(parseFailure.BytePositionInLine)}",
        _ => ex.GetType().Name,
    };

    // Both are nullable on JsonException: one constructed by a converter rather than raised
    // by the reader carries no position at all, and an absent position is said, not defaulted.
    private static string Position(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "unknown";
}
