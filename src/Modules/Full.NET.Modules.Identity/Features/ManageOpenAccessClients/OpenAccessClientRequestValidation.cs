using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.Identity.Features.ManageOpenAccessClients;

/// <summary>接入方应用请求中的用户标识解析；避免无效 GUID 在模型绑定阶段触发 500。</summary>
internal static class OpenAccessClientRequestValidation
{
    internal static Result<Guid?> ParseOptionalUserId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result<Guid?>.Success(null);
        }

        if (!Guid.TryParse(value.Trim(), out var userId) || userId == Guid.Empty)
        {
            return Result<Guid?>.Failure(InvalidUserId());
        }

        return Result<Guid?>.Success(userId);
    }

    internal static Result<string> ParseRequiredUsername(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result<string>.Failure(InvalidUsername());
        }

        var username = value.Trim();
        if (username.Length is < 3 or > 128)
        {
            return Result<string>.Failure(InvalidUsername());
        }

        return Result<string>.Success(username);
    }

    internal static string? NormalizeOptionalFilter(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Error InvalidUserId() =>
        new(
            ValidationErrorCodes.Failed,
            "User id must be a valid GUID.",
            ErrorType.Validation);

    private static Error InvalidUsername() =>
        new(
            ValidationErrorCodes.Failed,
            "Username must be 3-128 characters.",
            ErrorType.Validation);
}
