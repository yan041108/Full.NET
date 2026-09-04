using Full.NET.Abstractions.Results;
using Full.NET.Modules.SerialNumbers.Contracts;
using Full.NET.Modules.SerialNumbers.Domain;

namespace Full.NET.Modules.SerialNumbers.Features.ManageHostSerialRules;

/// <summary>
/// 使用受限 Pattern 生成无副作用预览；本服务不依赖数据库或计数器。
/// </summary>
internal sealed class SerialNumberPreviewService
{
    public Result<SerialNumberPreviewResponse> Preview(
        PreviewSerialNumberRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var parsed = SerialNumberPattern.Parse(request.Pattern, request.Scope);
        if (!parsed.IsSuccess)
        {
            return Result<SerialNumberPreviewResponse>.Failure(parsed.Error!);
        }

        try
        {
            var resetBucket = SerialNumberResetBucket.Create(
                request.ResetInterval,
                request.AtUtc);
            return Result<SerialNumberPreviewResponse>.Success(
                new SerialNumberPreviewResponse(
                    parsed.Value!.Format(
                        request.AtUtc,
                        request.TenantIdentifier,
                        request.SequenceValue),
                    resetBucket,
                    request.SequenceValue));
        }
        catch (ArgumentException)
        {
            return Result<SerialNumberPreviewResponse>.Failure(new Error(
                SerialNumberErrorCodes.PatternInvalid,
                "The serial number preview is invalid.",
                ErrorType.Validation));
        }
    }
}
