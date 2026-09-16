using Agent.Composition;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Agent.Tests.Composition;

public class VendorRateLimitGatesTests
{
    // The vendor's limits are per model, so every client of one model shares one gate: a composer
    // and a judge on the same model with a gate each would each fill ninety percent of one limit.
    [Fact]
    public void For_SameModelTwice_ReturnsTheSameGate()
    {
        var gates = new VendorRateLimitGates(new FakeTimeProvider());

        Assert.Same(gates.For("gpt-4o"), gates.For("gpt-4o"));
    }

    // Different models have limits of their own, so they do not wait on each other.
    [Fact]
    public void For_DifferentModels_ReturnsDifferentGates()
    {
        var gates = new VendorRateLimitGates(new FakeTimeProvider());

        Assert.NotSame(gates.For("gpt-4o"), gates.For("gpt-4o-mini"));
    }

    // The vendor's model name is the same key whatever case it is typed in: a judge pinned to the
    // lowercase constant and a composer configured with the vendor's own "GPT-4o" branding still
    // name one model, and would otherwise each fill ninety percent of its limit on their own gate.
    [Fact]
    public void For_SameModelDifferentCase_ReturnsTheSameGate()
    {
        var gates = new VendorRateLimitGates(new FakeTimeProvider());

        Assert.Same(gates.For("gpt-4o"), gates.For("GPT-4o"));
    }
}
