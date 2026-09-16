using System.Text.Json.Serialization;

namespace Full.NET.Modules.Identity.Http;

/// <summary>协议错误的静态 JSON 契约；字段名不受业务 camelCase 策略影响。</summary>
/// <param name="Error">OAuth 协议错误码。</param>
/// <param name="ErrorDescription">不包含内部异常或秘密的公开描述。</param>
internal sealed record IdentityOidcProtocolError(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("error_description")] string ErrorDescription);
