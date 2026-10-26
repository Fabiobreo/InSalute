namespace InSalute.Core.Users;

public record OperationResult(bool Succeeded, string? Error = null)
{
    public static OperationResult Ok() => new(true);

    public static OperationResult Fail(string error) => new(false, error);
}

public record OperationResult<T>(bool Succeeded, string? Error = null, T? Value = default)
{
    public static OperationResult<T> Ok(T value) => new(true, null, value);

    public static OperationResult<T> Fail(string error) => new(false, error);
}
