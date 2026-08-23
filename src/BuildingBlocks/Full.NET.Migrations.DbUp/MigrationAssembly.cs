using System.Reflection;

namespace Full.NET.Migrations.DbUp;

/// <summary>
/// 标记包含嵌入资源迁移脚本的程序集；脚本按 <c>Migrations.SqlServer</c> 与 <c>Migrations.MySql</c> 子目录成对存放。
/// </summary>
/// <remarks>
/// <para>承载脚本的程序集即本类所在程序集；脚本以 <c>.sql</c> 嵌入资源形式分发，
/// 由 <see cref="DbUpMigrationRunner"/> 按当前 Provider 片段过滤后交付 DbUp 执行。</para>
/// <para>新增脚本必须成对提供 SQL Server 与 MySQL 实现，并保持相同业务编号前缀；
/// 不允许只补单库脚本，也不允许把迁移脚本拆到其他程序集再追加路径。</para>
/// </remarks>
internal static class MigrationAssembly
{
    /// <summary>
    /// 取得承载全部嵌入资源迁移脚本的程序集。
    /// </summary>
    public static Assembly Value { get; } = typeof(MigrationAssembly).Assembly;
}
