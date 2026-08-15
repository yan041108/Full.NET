using System.Data;
using global::Dapper;

namespace Full.NET.Data.Dapper;

/// <summary>
/// Dapper Guid TypeHandler，实现 MySQL BINARY(16) 与 C# <see cref="Guid"/> 的双向互转，
/// 强制使用 <b>RFC 9562 Big-Endian（大端）字节序</b>，并门禁禁止未分配 Guid（Empty）入库。
/// </summary>
/// <remarks>
/// <para><b>字节序转换不变量（Endianness Invariant）：</b></para>
/// <para>
/// .NET <see cref="Guid"/> 默认构造函数采用混合端序（Data1/Data2/Data3 为小端，Data4 为大端），
/// 与 MySQL UUID() / RFC 9562（原 UUID v4/v7）规定的大端存储不一致。
/// 本 Handler 使用 <c>new Guid(bytes, bigEndian: true)</c> 构造，确保 16 字节数组在网络/数据库层
/// 与 Guid 逻辑表示一一对应，避免数据库端 UUID_TO_BIN() / BIN_TO_UUID() 解出错误值。
/// </para>
/// <para><b>支持的读取来源（Parse）：</b></para>
/// <list type="bullet">
/// <item><term>Guid 实例</term><description>直接透传，SQL Server Provider 原生映射场景。</description></item>
/// <item><term>string</term><description>兼容 Char(36) 遗留列，通过 <see cref="Guid.TryParse"/> 解析。</description></item>
/// <item><term>byte[16]</term><description>MySQL BINARY(16) 读取主路径，强制 bigEndian=true。</description></item>
/// </list>
/// <para><b>写入门禁（SetValue）：</b>
/// Guid.Empty 视为"未由应用预分配标识"，立即抛出 <see cref="ArgumentException"/>，
/// 防止数据库层生成的隐式默认值绕过应用层 IdGenerator（导致 CDC/Outbox 拿不到确定性主键）。</para>
/// <para><b>注册前置条件：</b>
/// 必须先调用 <c>SqlMapper.RemoveTypeMap(typeof(Guid))</c> 清除 Dapper 内置 Guid 映射，
/// 否则 Dapper 会优先使用内置映射跳过本 TypeHandler。详见 <see cref="ServiceCollectionExtensions.AddFullNetDapper"/>。</para>
/// </remarks>
internal sealed class AssignedGuidTypeHandler : SqlMapper.TypeHandler<Guid>
{
    /// <summary>
    /// 将数据库返回值解析为 C# Guid；支持 Guid / string / byte[16] 三种来源。
    /// </summary>
    /// <param name="value">Dapper 从数据读取器拿到的原始值。</param>
    /// <returns>解析后的 Guid（RFC 9562 大端对齐）。</returns>
    /// <exception cref="DataException">当 value 的类型无法转换为 Guid 时抛出。</exception>
    public override Guid Parse(object value) => value switch
    {
        Guid guid => guid,
        string text when Guid.TryParse(text, out var guid) => guid,
        byte[] bytes when bytes.Length == 16 => new Guid(bytes, bigEndian: true),
        _ => throw new DataException(
            $"Cannot convert {value.GetType().FullName} to Guid."),
    };

    /// <summary>
    /// 将 Guid 设置为数据库参数值；门禁禁止 Guid.Empty 入库。
    /// </summary>
    /// <param name="parameter">Dapper 创建的 IDbDataParameter。</param>
    /// <param name="value">待写入的 Guid，必须非 Empty。</param>
    /// <exception cref="ArgumentException">当 value 为 <see cref="Guid.Empty"/> 时抛出。</exception>
    public override void SetValue(IDbDataParameter parameter, Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("持久化标识必须由应用预先分配。", nameof(value));
        }

        parameter.DbType = DbType.Guid;
        parameter.Value = value;
    }
}
