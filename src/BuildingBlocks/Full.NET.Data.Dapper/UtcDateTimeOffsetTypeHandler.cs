using System.Data;
using global::Dapper;

namespace Full.NET.Data.Dapper;

/// <summary>
/// Dapper DateTimeOffset TypeHandler，在读写两端强制执行 UTC 标准化，
/// 确保跨 Provider（SQL Server / MySQL）与时区配置下时间戳的语义一致性。
/// </summary>
/// <remarks>
/// <para><b>读取标准化（Parse）：</b></para>
/// <list type="bullet">
/// <item>DateTimeOffset 源：无条件调用 <see cref="DateTimeOffset.ToUniversalTime"/>，
/// 消除应用层误传入 Local Offset 的风险。</item>
/// <item>DateTime 源：强制以 <see cref="DateTimeKind.Utc"/> 重新指定 Kind 后构造 DateTimeOffset。
/// MySQL DATETIME 列无偏移量元信息，约定存储值本身即为 UTC，应用层不得写入 Local Time。</item>
/// </list>
/// <para><b>写入标准化（SetValue）：</b>
/// 仅写入 <see cref="DateTimeOffset.UtcDateTime"/>（即不带 Offset 信息的 UTC DateTime），
/// 避免 SQL Server DATETIMEOFFSET 列与 MySQL DATETIME 列在迁移/CDC 时产生语义漂移。</para>
/// <para><b>不变量 Invariant：</b>
/// 本 Handler 处理后的所有时间戳 Offset 恒为 Zero（UTC），任何依赖本地时区的消费方必须显式转换。</para>
/// </remarks>
internal sealed class UtcDateTimeOffsetTypeHandler : SqlMapper.TypeHandler<DateTimeOffset>
{
    /// <summary>
    /// 将数据库返回值解析为 UTC 标准化的 DateTimeOffset。
    /// </summary>
    /// <param name="value">Dapper 从数据读取器拿到的原始值（DateTimeOffset 或 DateTime）。</param>
    /// <returns>Offset 恒为 Zero 的 UTC DateTimeOffset。</returns>
    /// <exception cref="DataException">当 value 的类型不在支持列表内时抛出。</exception>
    public override DateTimeOffset Parse(object value) => value switch
    {
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToUniversalTime(),
        DateTime dateTime => new DateTimeOffset(
            DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
        _ => throw new DataException(
            $"Cannot convert {value.GetType().FullName} to DateTimeOffset."),
    };

    /// <summary>
    /// 将 DateTimeOffset 以 UTC DateTime 形式写入数据库参数（剥离 Offset 信息）。
    /// </summary>
    /// <param name="parameter">Dapper 创建的 IDbDataParameter。</param>
    /// <param name="value">待写入的 DateTimeOffset；写入前自动取 UtcDateTime。</param>
    public override void SetValue(IDbDataParameter parameter, DateTimeOffset value)
    {
        parameter.Value = value.UtcDateTime;
    }
}
