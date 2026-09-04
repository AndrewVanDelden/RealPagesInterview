namespace Agent.Common;

public sealed record Option<TValue>
{
    private Option(TValue? value, bool hasValue)
    {
        Value = value;
        HasValue = hasValue;
    }

    public TValue? Value { get; }

    public bool HasValue { get; }

    public static Option<TValue> Some(TValue value) => new(value, true);

    public static Option<TValue> None() => new(default, false);
}
