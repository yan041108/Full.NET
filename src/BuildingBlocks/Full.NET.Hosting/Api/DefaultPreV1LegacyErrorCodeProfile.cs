namespace Full.NET.Hosting.Api;

/// <summary>
/// 默认关闭 legacy error_code 对外回退，标准 API 与兼容层均输出 canonical。
/// </summary>
public sealed class DefaultPreV1LegacyErrorCodeProfile : IPreV1LegacyErrorCodeProfile
{
    /// <summary>
    /// 默认不对外输出 Pre-v1 legacy error_code，始终返回 false；
    /// 标准 API 与兼容层均输出 canonical 错误码。
    /// </summary>
    public bool EmitLegacyErrorCodes => false;
}
