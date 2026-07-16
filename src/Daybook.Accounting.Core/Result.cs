namespace Daybook.Accounting.Core;

/// <summary>
/// The outcome of an operation that can fail with an expected, actionable
/// <see cref="Core.Error"/>. Core and Application return this instead of
/// throwing for business-rule violations (spec §10, CLAUDE.md golden rule 6);
/// exceptions stay reserved for genuine infrastructure faults or caller bugs.
/// </summary>
public readonly struct Result<T>
{
    private readonly T? _value;
    private readonly Error? _error;

    private Result(T value)
    {
        _value = value;
        _error = null;
        IsSuccess = true;
    }

    private Result(Error error)
    {
        _value = default;
        _error = error;
        IsSuccess = false;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    /// <exception cref="InvalidOperationException">The result is a failure.</exception>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Result has no value; it failed. Check IsSuccess first.");

    /// <exception cref="InvalidOperationException">The result is a success.</exception>
    public Error Error => IsFailure
        ? _error!
        : throw new InvalidOperationException("Result has no error; it succeeded. Check IsFailure first.");

    public static Result<T> Success(T value) => new(value);

    public static Result<T> Failure(Error error) => new(error);

    public static implicit operator Result<T>(T value) => Success(value);

    public static implicit operator Result<T>(Error error) => Failure(error);
}