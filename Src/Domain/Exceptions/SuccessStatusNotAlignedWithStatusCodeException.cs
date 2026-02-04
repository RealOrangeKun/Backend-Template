namespace Domain.Exceptions;

public class SuccessStatusNotAlignedWithStatusCodeException(Dictionary<string, string[]> errors) : 
    CustomAppException("Success status is not aligned with the provided status code.")
{
    public override int StatusCode => 500;
    public override string ErrorCode => "SUCCESS_STATUS_NOT_ALIGNED_WITH_STATUS_CODE";
    public override Dictionary<string, string[]> Errors { get; set; } = errors;
}