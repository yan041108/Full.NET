namespace Full.NET.Migrations.DbUp;

/// <summary>
/// 表示一次数据库迁移的执行结果；不包含 SQL 文本、连接信息或异常堆栈。
/// </summary>
/// <param name="Successful">整体迁移是否成功；任一脚本失败即为 <see langword="false"/>。</param>
/// <param name="ExecutedScriptCount">本次实际执行的脚本数量，未执行的已记账脚本不计入。</param>
public sealed record MigrationResult(bool Successful, int ExecutedScriptCount);

/// <summary>
/// 数据库迁移入口；Migrator 通过该接口按当前 Provider 加载 SQL 脚本、执行迁移并记账。
/// </summary>
/// <remarks>
/// <para>Full.NET 同时支持 SQL Server 与 MySQL，迁移脚本按 <c>Migrations.SqlServer</c> 与
/// <c>Migrations.MySql</c> 子目录成对存放，由实现按 <see cref="Full.NET.Data.Abstractions.DatabaseProvider"/>
/// 选择对应脚本集合；SQL 与索引差异必须经双库测试验证，不得以“语法相近”代替。</para>
/// <para>已执行脚本通过 DbUp Journal 表持久化记账，重复执行时只运行未记账的新脚本；
/// 迁移失败必须修复后重跑，<see cref="MigrationResult"/> 不携带可重试或可回滚语义。</para>
/// <para>API Host 不得引用或调用本接口；迁移只能由 Migrator 在数据库可达且权限受限的部署单元中执行。</para>
/// </remarks>
public interface IDatabaseMigrationRunner
{
    /// <summary>
    /// 按当前配置的数据库 Provider 执行全部未记账迁移脚本，并返回执行结果。
    /// </summary>
    /// <param name="cancellationToken">用于取消迁移的令牌；DbUp 内部按脚本粒度检查取消，已执行脚本不会被自动回滚。</param>
    /// <returns>承载成功标志与本次执行脚本数的迁移结果；迁移失败时抛出异常而非返回失败结果。</returns>
    Task<MigrationResult> MigrateAsync(CancellationToken cancellationToken = default);
}
