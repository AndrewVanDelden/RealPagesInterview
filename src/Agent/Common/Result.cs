namespace Agent.Common;

public sealed record Result<TValue>
{
    private Result(TValue? value, string? error, bool isSuccess)
    {
        Value = value;
        Error = error;
        IsSuccess = isSuccess;
    }

    public TValue? Value { get; }

    public string? Error { get; }

    public bool IsSuccess { get; }

    public static Result<TValue> Success(TValue value) => new(value, null, true);

    public static Result<TValue> Failure(string error) => new(default, error, false);
}
