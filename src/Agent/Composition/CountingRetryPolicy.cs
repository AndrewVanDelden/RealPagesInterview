using System.ClientModel.Primitives;

namespace Agent.Composition;

// The SDK's own retry policy, counting the attempts it makes (D28, playbook step 49: the
// retry count is visible in the diagnostics). The policy is the pipeline's, so the count is
// a running total for the client; OpenAiCompletionClient reads the difference across one
// call. Known limit, stated rather than hidden: that difference is only this call's when the
// client is used one call at a time, which is what the CLI's sequential batch loop does. A
// concurrent caller would see another call's retries mixed in.
internal sealed class CountingRetryPolicy(int maxRetries) : ClientRetryPolicy(maxRetries)
{
    private int attempts;

    public int Attempts => Volatile.Read(ref attempts);

    // Only the async hook is overridden: OpenAiCompletionClient calls CompleteChatAsync and
    // nothing else, so the synchronous path is never taken and an override on it would be a
    // line no honest test could reach.
    protected override ValueTask OnSendingRequestAsync(PipelineMessage message)
    {
        Interlocked.Increment(ref attempts);
        return base.OnSendingRequestAsync(message);
    }

    // D33: retry a response whose status the SDK classifies as transient, and never an attempt
    // that ended in an exception. The base classification is PipelineMessageClassifier.Default,
    // documented in the restored System.ClientModel 1.14.0 as 408, 429, 500, 502, 503 and 504,
    // and it is what bounds the count at maxRetries. An exception here is a timeout, a caller's
    // cancellation or a request that got no response at all: a timeout says the completion did
    // not fit the budget, and a second identical call inside the same budget has no mechanism
    // by which it would, while about a third of abandoned attempts are billed in full (D31
    // addendum). One attempt that throws also means the pipeline rethrows that one exception
    // rather than an AggregateException of every attempt's, which the composer does not catch.
    // Async only, for the reason OnSendingRequestAsync gives above.
    protected override ValueTask<bool> ShouldRetryAsync(PipelineMessage message, Exception? exception) =>
        exception is null ? base.ShouldRetryAsync(message, exception) : ValueTask.FromResult(false);
}
