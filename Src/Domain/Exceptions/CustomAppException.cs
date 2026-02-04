namespace Domain.Exceptions;

public abstract class CustomAppException(string message) : Exception(message)
{
    public abstract int StatusCode { get; }
    public abstract string ErrorCode { get; }
    public abstract Dictionary<string, string[]> Errors { get; set; }
}