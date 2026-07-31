namespace Full.NET.Data.CodeGeneration.Schema;

/// <summary>
/// 保存生成期可验证的实体生命周期、审计、并发和所有权能力。
/// </summary>
/// <param name="DeleteMode">删除与不可变生命周期策略。</param>
/// <param name="HasCreatedAudit">是否包含创建时间和创建人审计。</param>
/// <param name="HasUpdatedAudit">是否包含更新时间和更新人审计。</param>
/// <param name="HasDeletedAudit">是否包含删除时间和删除人审计。</param>
/// <param name="HasVersion">是否使用 Version 乐观并发。</param>
/// <param name="OwnershipMode">业务所有权策略。</param>
public sealed record FullNetCrudEntityCapabilities(
    FullNetCrudDeleteMode DeleteMode,
    bool HasCreatedAudit,
    bool HasUpdatedAudit,
    bool HasDeletedAudit,
    bool HasVersion,
    FullNetCrudOwnershipMode OwnershipMode)
{
    /// <summary>获取生成器是否允许产生更新操作。</summary>
    public bool CanUpdate => DeleteMode != FullNetCrudDeleteMode.Immutable;

    /// <summary>获取生成器是否允许产生删除操作。</summary>
    public bool CanDelete => DeleteMode != FullNetCrudDeleteMode.Immutable;

    internal static FullNetCrudEntityCapabilities FromLegacy(
        bool hasVersion) =>
        new(
            FullNetCrudDeleteMode.HardDelete,
            HasCreatedAudit: false,
            HasUpdatedAudit: false,
            HasDeletedAudit: false,
            HasVersion: hasVersion,
            FullNetCrudOwnershipMode.None);
}
