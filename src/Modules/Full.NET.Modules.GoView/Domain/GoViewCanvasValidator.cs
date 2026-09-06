using System.Text.Json;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.GoView.Contracts;

namespace Full.NET.Modules.GoView.Domain;

/// <summary>校验 GoView 画布 JSON 的结构与大小边界。</summary>
internal static class GoViewCanvasValidator
{
    /// <summary>校验画布 JSON 是否为受支持的对象结构。</summary>
    public static Result<bool> Validate(string canvasJson)
    {
        if (string.IsNullOrWhiteSpace(canvasJson))
        {
            return Invalid("Canvas JSON is required.");
        }

        if (canvasJson.Length > GoViewCanvasPolicy.MaxCanvasJsonLength)
        {
            return Result<bool>.Failure(new Error(
                GoViewErrorCodes.CanvasJsonInvalid,
                "Canvas JSON exceeds the maximum allowed length.",
                ErrorType.Validation));
        }

        try
        {
            using var document = JsonDocument.Parse(canvasJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Invalid("Canvas JSON must be a JSON object.");
            }

            if (!document.RootElement.TryGetProperty("width", out var width) ||
                !document.RootElement.TryGetProperty("height", out var height) ||
                width.ValueKind != JsonValueKind.Number ||
                height.ValueKind != JsonValueKind.Number ||
                width.GetInt32() <= 0 ||
                height.GetInt32() <= 0)
            {
                return Invalid("Canvas JSON must include positive width and height.");
            }

            if (!document.RootElement.TryGetProperty("components", out var components) ||
                components.ValueKind != JsonValueKind.Array)
            {
                return Invalid("Canvas JSON must include a components array.");
            }
        }
        catch (JsonException)
        {
            return Invalid("Canvas JSON is not valid JSON.");
        }

        return Result<bool>.Success(true);
    }

    /// <summary>规范化可选画布 JSON；空值时返回默认空白画布。</summary>
    public static string NormalizeOptional(string? canvasJson)
    {
        if (string.IsNullOrWhiteSpace(canvasJson))
        {
            return GoViewCanvasPolicy.DefaultCanvasJson;
        }

        return canvasJson.Trim();
    }

    private static Result<bool> Invalid(string message) =>
        Result<bool>.Failure(new Error(
            GoViewErrorCodes.CanvasJsonInvalid,
            message,
            ErrorType.Validation));
}
