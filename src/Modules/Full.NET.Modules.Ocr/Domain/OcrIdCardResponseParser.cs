using System.Text.Json;

namespace Full.NET.Modules.Ocr.Domain;

/// <summary>PaddleOCR 身份证识别 HTTP 响应解析。</summary>
internal static class OcrIdCardResponseParser
{
    /// <summary>解析 Provider JSON 响应为结构化字段。</summary>
    public static bool TryParse(string responseBody, out OcrIdCardParsedResult result, out string message)
    {
        result = default!;
        message = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            var data = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var dataElement)
                ? dataElement
                : root;

            if (data.ValueKind != JsonValueKind.Object)
            {
                message = "OCR response does not contain a JSON object.";
                return false;
            }

            var name = ReadString(data, "name", "Name");
            var idNumber = ReadString(data, "id_number", "idNumber", "IdNumber", "idCard", "IdCard");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(idNumber))
            {
                message = "OCR response is missing name or id number.";
                return false;
            }

            result = new OcrIdCardParsedResult(
                name.Trim(),
                idNumber.Trim(),
                ReadString(data, "gender", "Gender"),
                ReadString(data, "nation", "Nation", "ethnicity", "Ethnicity"),
                ReadString(data, "address", "Address"),
                ReadString(data, "birth_date", "birthDate", "BirthDate"));
            message = "Recognition parsed successfully.";
            return true;
        }
        catch (JsonException)
        {
            message = "OCR response is not valid JSON.";
            return false;
        }
    }

    private static string? ReadString(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (element.TryGetProperty(propertyName, out var value)
                && value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return null;
    }
}

/// <summary>身份证 OCR 解析结果。</summary>
internal sealed record OcrIdCardParsedResult(
    string Name,
    string IdNumber,
    string? Gender,
    string? Nation,
    string? Address,
    string? BirthDate);
