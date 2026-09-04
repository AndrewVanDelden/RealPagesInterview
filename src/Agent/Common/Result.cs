namespace Agent.Common;

public sealed record Result<TValue>
{
    private readonly TValue? _value;
    private readonly string? _error;

    private Result(TValue? value, string? error, bool isSuccess)
    {
        _value = value;
        _error = error;
        IsSuccess = isSuccess;
    }

    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Result has no value because it is a failure.");

    public string Error => !IsSuccess
        ? _error!
        : throw new InvalidOperationException("Result has no error because it is a success.");

    public bool IsSuccess { get; }

    public static Result<TValue> Success(TValue value) => new(value, null, true);

    public static Result<TValue> Failure(string error) => new(default, error, false);
}
