using System.ClientModel.Primitives;
using System.Runtime.CompilerServices;

namespace Agent.Composition;

// The SDK's own retry policy, counting the attempts it makes (D28, playbook step 49: the
// retry count is visible in the diagnostics). D37: the count is per call. It was a running
// total for the client, read as a difference across one call, and two calls in flight on one
// client each saw the other's attempts in that difference. The client opens a holder per call
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
