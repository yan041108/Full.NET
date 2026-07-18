namespace Full.NET.Data.Abstractions;

/// <summary>
/// 定义 MySQL UUID 在迁移窗口内允许使用的物理存储模式。
/// </summary>
public enum MySqlGuidStorageMode
{
    /// <summary>
    /// 使用 MySqlConnector 默认的 CHAR(36) 映射，仅供 001-008 迁移过渡期使用。
    /// </summary>
    LegacyChar36 = 0,

    /// <summary>
    /// 使用 RFC 9562 网络字节序的 BINARY(16) 映射。
    /// </summary>
    Binary16 = 1,
}
