using System.Text.Json;

namespace Full.NET.Modules.K3Cloud.Domain;

/// <summary>解析金蝶 K3Cloud WebAPI 响应中的登录、保存与提交结果。</summary>
internal static class K3CloudResponseParser
{
    /// <summary>解析 ValidateUser 响应是否成功。</summary>
    public static bool TryParseLoginSuccess(string responseBody, out string message)
    {
        message = string.Empty;
        if (!TryParseJson(responseBody, out var root))
        {
            message = "K3Cloud login response is not valid JSON.";
            return false;
        }

        if (root.TryGetProperty("LoginResultType", out var loginResult)
            && loginResult.ValueKind == JsonValueKind.Number
            && loginResult.GetInt32() == 1)
        {
            message = "ValidateUser succeeded.";
            return true;
        }

        if (root.TryGetProperty("Message", out var messageElement)
            && messageElement.ValueKind == JsonValueKind.String)
        {
            message = messageElement.GetString() ?? "ValidateUser failed.";
            return false;
        }

        message = "ValidateUser failed.";
        return false;
    }

    /// <summary>解析 Save 响应中的单据标识与编号。</summary>
    public static bool TryParseSaveResult(string responseBody, out string billId, out string billNo, out string message)
    {
        billId = string.Empty;
        billNo = string.Empty;
        message = string.Empty;
        if (!TryParseJson(responseBody, out var root))
        {
            message = "K3Cloud save response is not valid JSON.";
            return false;
        }

        if (!TryGetResultObject(root, out var result))
        {
            message = "K3Cloud save response is missing Result.";
            return false;
        }

        if (!IsResponseSuccess(result, out message))
        {
            return false;
        }

        if (result.TryGetProperty("Id", out var idElement))
        {
            billId = idElement.ValueKind == JsonValueKind.Number
                ? idElement.GetRawText()
                : idElement.GetString() ?? string.Empty;
        }

        if (result.TryGetProperty("Number", out var numberElement)
            && numberElement.ValueKind == JsonValueKind.String)
        {
            billNo = numberElement.GetString() ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(billId) && string.IsNullOrWhiteSpace(billNo))
        {
            message = "K3Cloud save succeeded but bill id/number was not returned.";
            return false;
        }

        message = "Save succeeded.";
        return true;
    }

    /// <summary>解析 Submit 响应是否成功。</summary>
    public static bool TryParseSubmitSuccess(string responseBody, out string message)
    {
        message = string.Empty;
        if (!TryParseJson(responseBody, out var root))
        {
            message = "K3Cloud submit response is not valid JSON.";
            return false;
        }

        if (!TryGetResultObject(root, out var result))
        {
            message = "K3Cloud submit response is missing Result.";
            return false;
        }

        return IsResponseSuccess(result, out message);
    }

    /// <summary>根据 Save 结果构造 Submit 请求 JSON。</summary>
    public static string BuildSubmitPayload(string billId, string billNo)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("CreateOrgId", 0);
            writer.WritePropertyName("Numbers");
            writer.WriteStartArray();
            if (!string.IsNullOrWhiteSpace(billNo))
            {
                writer.WriteStringValue(billNo);
            }

            writer.WriteEndArray();
            writer.WriteString("Ids", billId);
            writer.WriteNumber("SelectedPostId", 0);
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static bool TryParseJson(string responseBody, out JsonElement root)
    {
        root = default;
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            root = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryGetResultObject(JsonElement root, out JsonElement result)
    {
        result = default;
        if (root.TryGetProperty("Result", out var resultElement)
            && resultElement.ValueKind == JsonValueKind.Object)
        {
            result = resultElement;
            return true;
        }

        return false;
    }

    private static bool IsResponseSuccess(JsonElement result, out string message)
    {
        message = string.Empty;
        if (!result.TryGetProperty("ResponseStatus", out var status)
            || status.ValueKind != JsonValueKind.Object)
        {
            message = "Operation failed.";
            return false;
        }

        if (status.TryGetProperty("IsSuccess", out var isSuccess)
            && isSuccess.ValueKind == JsonValueKind.True)
        {
            message = "Operation succeeded.";
            return true;
        }

        if (status.TryGetProperty("Errors", out var errors)
            && errors.ValueKind == JsonValueKind.Array
            && errors.GetArrayLength() > 0
            && errors[0].TryGetProperty("Message", out var errorMessage)
            && errorMessage.ValueKind == JsonValueKind.String)
        {
            message = errorMessage.GetString() ?? "Operation failed.";
            return false;
        }

        message = "Operation failed.";
        return false;
    }
}
