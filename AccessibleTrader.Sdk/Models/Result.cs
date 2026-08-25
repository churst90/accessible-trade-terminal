namespace AccessibleTrader.Sdk.Models
{
    public class Result
    {
        public bool IsSuccess { get; }
        public string? ErrorMessage { get; }
        public bool IsFailure => !IsSuccess;

        protected Result(bool isSuccess, string? errorMessage)
        {
            IsSuccess = isSuccess;
            ErrorMessage = errorMessage;
        }

        public static Result Success() => new Result(true, null);
        public static Result Failure(string message) => new Result(false, message);
    }

    public class Result<T> : Result
    {
        public T? Value { get; }

        private Result(bool isSuccess, T? value, string? errorMessage) : base(isSuccess, errorMessage)
        {
            Value = value;
        }

        public static Result<T> Success(T value) => new Result<T>(true, value, null);
        public static new Result<T> Failure(string message) => new Result<T>(false, default, message);

        public static implicit operator Result<T>(T value) => Success(value);
    }
}
