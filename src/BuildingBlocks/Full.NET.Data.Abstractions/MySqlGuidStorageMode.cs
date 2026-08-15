namespace Full.NET.Data.Abstractions;

/// <summary>
/// 定义 MySQL UUID 在迁移窗口内允许使用的物理存储模式。
/// </summary>
/// <remarks>
/// <para>
/// MySQL 没有原生 UUID 类型，历史上使用 CHAR(36) 以人类可读格式存储 UUID v4。
/// 这种模式占用 36 字节 + 排序规则开销，索引效率低（随机分布导致 B-Tree 分裂频繁），
/// 且不支持 RFC 9562（原 Peaberry UUID）提出的 time-ordered 布局。
/// </para>
/// <para>
/// 生产环境必须使用 <see cref="Binary16"/> 模式，该模式采用 RFC 9562 规定的
/// 网络字节序（大端序，big-endian）存储 16 字节 UUID，与 MySqlConnector 的
/// OldGuids=false + GuidFormat=Binary16 组合完全对齐，确保跨语言、跨系统的
/// 字节一致性。LegacyChar36 仅作为 001-008 号数据迁移期间的兼容选项，不应
/// 在新部署中启用。
/// </para>
/// </remarks>
public enum MySqlGuidStorageMode
{
    /// <summary>
    /// 使用 MySqlConnector 默认的 CHAR(36) 映射，仅供 001-008 迁移过渡期使用。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 存储格式：xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx 的 36 字符 UTF-8 文本。
    /// 该模式存在以下已知问题：
    /// 1) 主键索引膨胀，二级索引包含完整主键，放大表体积 2.25x；
    /// 2) UUID v4 随机性导致插入热点与页分裂，写吞吐下降 40% 以上；
    /// 3) 不支持 RFC 9562 时间前缀索引范围查询。
    /// </para>
    /// <para>
    /// 迁移结束后必须通过配置项将其切换为 <see cref="Binary16"/>，运维脚本会
    /// 同步变更列类型并重建索引。遗留数据在迁移期由双向触发器保证读一致性。
    /// </para>
    /// </remarks>
    LegacyChar36 = 0,

    /// <summary>
    /// 使用 RFC 9562 网络字节序的 BINARY(16) 映射，是唯一受生产支持的存储模式。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 字节顺序严格遵循 RFC 9562 Section 5.1 的网络字节序（大端），即：
    /// time_high（前 4 字节，大端）→ time_mid（2 字节，大端）→ time_low_and_version
    /// （2 字节，大端）→ clock_seq（2 字节，大端）→ node（6 字节）。
    /// 对于 UUID v7，time_high/mid 是单调递增的 Unix 毫秒时间戳，保证插入顺序与
    /// 索引顺序一致，消除写放大。
    /// </para>
    /// <para>
    /// 不变量：所有 Guid 类型参数在进入 Dapper 前必须由 DbConnection 设置层完成
    /// Guid → BigEndian 16 字节转换，禁止业务层手动 BitConverter 转换后传入 byte[]。
    /// </para>
    /// </remarks>
    Binary16 = 1,
}
