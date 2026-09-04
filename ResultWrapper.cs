namespace LegalHoldAdmin.Models;

public sealed class ResultWrapper<T>
{
    private ResultWrapper()
    {
    }

    public bool IsSuccess { get; private init; }
    public T? Data { get; private init; }
    public string? ErrorMessage { get; private init; }

    public static ResultWrapper<T> Success(T data)
    {
        return new ResultWrapper<T>
        {
            IsSuccess = true,
            Data = data
        };
    }

    public static ResultWrapper<T> Failure(string errorMessage, T? data = default)
    {
        return new ResultWrapper<T>
        {
            IsSuccess = false,
            ErrorMessage = errorMessage,
            Data = data
        };
    }
}
