using System.ClientModel;
using System.Net;
using System.Text.Json;
using Agent.Common;
using Agent.Composition;
using Agent.Tests.TestSupport;
using Xunit;

namespace Agent.Tests.Common;

public class ExceptionFormattingTests
{
    [Fact]
    public void ToDiagnosticString_ReturnsExceptionTypeNameAndMessage()
    {
        var exception = new InvalidOperationException("boom");

        string result = exception.ToDiagnosticString();

        Assert.Equal("InvalidOperationException: boom", result);
    }

    // Playbook step 68. The SDK copies the vendor's raw error response body into
    // ClientResultException.Message verbatim, so the exception is built through the real
    // pipeline over a scripted transport rather than constructed by hand: a hand-made one
    // would only prove the formatter reads a property this project made up.
    [Fact]
    public async Task ToRedactedDiagnosticString_VendorFailure_NamesTheStatusAndNotTheBody()
    {
        var handler = new FakeHttpMessageHandler(
            (HttpStatusCode.Unauthorized, """{"error":{"message":"VENDOR-BODY-MARKER for key sk-live-abc"}}"""));
        using var httpClient = new HttpClient(handler);
        ICompletionClient client = new OpenAiCompletionClient(httpClient, "fake-key");

        ClientResultException vendorFailure =
            await Assert.ThrowsAsync<ClientResultException>(() => client.CompleteAsync("system", "user"));

        // The control for the assertion below: the body really is in the exception.
        Assert.Contains("VENDOR-BODY-MARKER", vendorFailure.Message);
        Assert.Equal("ClientResultException: HTTP status 401", vendorFailure.ToRedactedDiagnosticString());
    }

    // The deserializer's message names the offending character and its path names the member
    // being read, which for a member no record type declares is a name the record authored.
    // Only the position survives.
    [Fact]
    public void ToRedactedDiagnosticString_ParseFailure_NamesThePositionAndNotTheMessageOrPath()
    {
        JsonException parseFailure = Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<Dictionary<string, string>>("""{"note_from_taylor":Taylor}"""));

        Assert.Contains("note_from_taylor", parseFailure.Message);
        Assert.Equal("JsonException: line 0, byte position 20", parseFailure.ToRedactedDiagnosticString());
    }

    // Both positions are nullable on JsonException: one raised by a converter rather than by
    // the reader carries neither, and an absent position is said rather than defaulted to a
    // number that would send a reader to the wrong place in the file.
    [Fact]
    public void ToRedactedDiagnosticString_ParseFailureWithNoPosition_SaysSoRatherThanInventingOne()
    {
        var parseFailure = new JsonException("the converter's own message");

        Assert.Equal("JsonException: line unknown, byte position unknown", parseFailure.ToRedactedDiagnosticString());
    }

    // Every other type reports its name alone. An Exception variable cannot promise who wrote
    // its Message, and the caller's own log message says which operation failed.
    [Fact]
    public void ToRedactedDiagnosticString_AnyOtherException_NamesTheTypeAlone()
    {
        var failure = new InvalidOperationException("no completion content");

        Assert.Equal("InvalidOperationException", failure.ToRedactedDiagnosticString());
    }
}
