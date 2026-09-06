using System.Text.Json;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.K3Cloud.Contracts;

namespace Full.NET.Modules.K3Cloud.Domain;

/// <summary>校验 K3Cloud 连接配置与单据同步载荷边界。</summary>
internal static class K3CloudFieldValidator
{
    public const int MaxNameLength = 128;
    public const int MaxBaseUrlLength = 512;
    public const int MaxAcctIdLength = 64;
    public const int MaxUsernameLength = 64;
    public const int MaxBusinessKeyLength = 128;
    public const int MaxPayloadJsonLength = 512 * 1024;

    public static Result<bool> ValidateConnectionMetadata(
        string name,
        string baseUrl,
        string acctId,
        string username,
        int lcid)
    {
        if (string.IsNullOrWhiteSpace(name)
            || string.IsNullOrWhiteSpace(baseUrl)
            || string.IsNullOrWhiteSpace(acctId)
            || string.IsNullOrWhiteSpace(username))
        {
            return InvalidConnection("Name, base URL, account id and username are required.");
        }

        if (name.Length > MaxNameLength
            || baseUrl.Length > MaxBaseUrlLength
            || acctId.Length > MaxAcctIdLength
            || username.Length > MaxUsernameLength)
        {
            return InvalidConnection("Connection metadata exceeds allowed length.");
        }

        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            return InvalidConnection("Base URL must be an absolute http or https URL.");
        }

        if (lcid <= 0)
        {
            return InvalidConnection("LCID must be a positive locale id.");
        }

        return Result<bool>.Success(true);
    }

    public static Result<bool> ValidateDocumentSync(
        string documentTypeKey,
        string businessKey,
        string payloadJson)
    {
        if (!K3CloudDocumentTypeCatalog.IsSupported(documentTypeKey))
        {
            return Result<bool>.Failure(new Error(
                K3CloudErrorCodes.DocumentTypeUnsupported,
                "The K3Cloud document type is not supported in this slice.",
                ErrorType.Validation));
        }

        if (string.IsNullOrWhiteSpace(businessKey) || businessKey.Length > MaxBusinessKeyLength)
        {
            return Result<bool>.Failure(new Error(
                K3CloudErrorCodes.DocumentSyncPayloadInvalid,
                "Business key is required and must not exceed the allowed length.",
                ErrorType.Validation));
        }

        if (string.IsNullOrWhiteSpace(payloadJson) || payloadJson.Length > MaxPayloadJsonLength)
        {
            return Result<bool>.Failure(new Error(
                K3CloudErrorCodes.DocumentSyncPayloadInvalid,
                "Payload JSON is required and must not exceed the allowed length.",
                ErrorType.Validation));
        }

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return InvalidPayload("Payload JSON must be a JSON object.");
            }
        }
        catch (JsonException)
        {
            return InvalidPayload("Payload JSON is not valid JSON.");
        }

        return Result<bool>.Success(true);
    }

    private static Result<bool> InvalidConnection(string message) =>
        Result<bool>.Failure(new Error(
            K3CloudErrorCodes.ConnectionInvalid,
            message,
            ErrorType.Validation));

    private static Result<bool> InvalidPayload(string message) =>
        Result<bool>.Failure(new Error(
            K3CloudErrorCodes.DocumentSyncPayloadInvalid,
            message,
            ErrorType.Validation));
}
