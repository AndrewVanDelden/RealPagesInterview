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
}
