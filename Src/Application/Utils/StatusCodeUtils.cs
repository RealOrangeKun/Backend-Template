namespace Application.Utils;
public static class StatusCodeUtils
{
    public static bool IsSuccess(int statusCode) => statusCode >= 200 && statusCode <= 299;
    public static bool IsFailure(int statusCode) => !IsSuccess(statusCode);

    public static bool IsNoBodyStatusCode(int statusCode) =>
        (statusCode >= 100 && statusCode <= 199) ||
        statusCode == 204 ||
        statusCode == 205 ||
        statusCode == 304;
}