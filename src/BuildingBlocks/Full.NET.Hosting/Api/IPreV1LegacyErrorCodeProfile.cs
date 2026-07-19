namespace Full.NET.Hosting.Api;

/// <summary>
/// 控制兼容适配器是否在对外包络中回退到 Pre-v1 legacy error_code。
/// </summary>
public interface IPreV1LegacyErrorCodeProfile
{
    /// <summary>
    /// 为 true 时，Admin.NET 等兼容层对外仍输出 legacy error_code。
    /// </summary>
    bool EmitLegacyErrorCodes { get; }
}
