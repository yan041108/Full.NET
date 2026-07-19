namespace Full.NET.Hosting.Api;

/// <summary>
/// 默认关闭 legacy error_code 对外回退，标准 API 与兼容层均输出 canonical。
/// </summary>
public sealed class DefaultPreV1LegacyErrorCodeProfile : IPreV1LegacyErrorCodeProfile
{
    /// <inheritdoc />
    public bool EmitLegacyErrorCodes => false;
}
