namespace Agent.Common;

public sealed record Option<TValue>
{
    private readonly TValue? _value;

    private Option(TValue? value, bool hasValue)
    {
        _value = value;
        HasValue = hasValue;
    }

    public TValue Value => HasValue
        ? _value!
        : throw new InvalidOperationException("Option has no value.");

    public bool HasValue { get; }

    public static Option<TValue> Some(TValue value) => new(value, true);

    public static Option<TValue> None() => new(default, false);
}
