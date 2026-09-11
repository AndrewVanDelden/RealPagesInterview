using System.ClientModel.Primitives;
using System.Runtime.CompilerServices;

namespace Agent.Composition;

// The SDK's own retry policy, counting the attempts it makes (playbook step 49: the retry
// count is visible in the diagnostics). The count is per call, because records run
// concurrently: a client-wide total read as a difference across one call would also count the
// attempts of another call in flight on the same client. The client opens a holder per call
// through BeginCall, carried by an AsyncLocal: the SDK runs its retry loop, and so the hook
// below, inside the async flow of the call that opened the holder, and each concurrent call
// has a flow of its own. An instance field carrying per-call state, not a static accessor
// standing in for constructor injection, which is AgentLog's alone.
internal sealed class CountingRetryPolicy(int maxRetries) : ClientRetryPolicy(maxRetries)
{
    private readonly AsyncLocal<StrongBox<int>?> currentCallAttempts = new();

    // Called by the client at the top of one call, inside that call's own async method, so the
    // holder flows into the SDK call beneath it and never out to the caller or to a sibling
    // call: an async method restores its caller's context when it first yields.
    public StrongBox<int> BeginCall()
    {
        var attempts = new StrongBox<int>();
        currentCallAttempts.Value = attempts;
        return attempts;
    }

    // Only the async hook is overridden: OpenAiCompletionClient calls CompleteChatAsync and
    // nothing else, so the synchronous path is never taken and an override on it would be a
    // line no honest test could reach. The holder is never null here: this policy is only ever
    // the pipeline of the one client that opens a holder before every call it makes. The
    // attempts of one call are sequential, so a plain increment is enough.
    protected override ValueTask OnSendingRequestAsync(PipelineMessage message)
    {
        currentCallAttempts.Value!.Value++;
        return base.OnSendingRequestAsync(message);
    }

    // Retry a response whose status the SDK classifies as transient, and never an attempt that
    // ended in an exception. The base classification, PipelineMessageClassifier.Default, is 408,
    // 429, 500, 502, 503 and 504 in the restored System.ClientModel 1.14.0, and it caps the count
    // at maxRetries. An exception is a timeout, a cancellation or no response at all: a second
    // identical call inside the same budget cannot fit where the first did not, and about a
    // third of abandoned attempts are billed in full. One throwing attempt also means the
    // pipeline rethrows that one exception, not an AggregateException the composer does not
    // catch. Async only, for the reason OnSendingRequestAsync gives above.
    protected override ValueTask<bool> ShouldRetryAsync(PipelineMessage message, Exception? exception) =>
        exception is null ? base.ShouldRetryAsync(message, exception) : ValueTask.FromResult(false);
}
