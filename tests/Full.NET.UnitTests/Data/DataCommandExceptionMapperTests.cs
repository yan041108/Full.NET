using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using MySqlConnector;

namespace Full.NET.UnitTests.Data;

/// <summary>
/// 验证 SQL Server 与 MySQL 的命令失败码统一映射为 Provider 无关类别。
/// </summary>
[TestClass]
public sealed class DataCommandExceptionMapperTests
{
    /// <summary>
    /// 验证 SQL Server 死锁编号映射为可重试的死锁类别。
    /// </summary>
    [TestMethod]
    public void ClassifySqlServer_maps_deadlock()
    {
        var kind = DataCommandExceptionMapper.ClassifySqlServer(1205);

        Assert.AreEqual(DataCommandFailureKind.Deadlock, kind);
    }

    /// <summary>
    /// 验证 MySQL 的事务死锁与用户锁死锁均映射为统一死锁类别。
    /// </summary>
    /// <param name="errorCode">MySQL 死锁错误码。</param>
    [TestMethod]
    [DataRow(MySqlErrorCode.LockDeadlock)]
    [DataRow(MySqlErrorCode.UserLockDeadlock)]
    public void ClassifyMySql_maps_deadlock(MySqlErrorCode errorCode)
    {
        var kind = DataCommandExceptionMapper.ClassifyMySql(errorCode);

        Assert.AreEqual(DataCommandFailureKind.Deadlock, kind);
    }

    /// <summary>
    /// 验证双数据库唯一键错误仍保持原有唯一约束分类。
    /// </summary>
    [TestMethod]
    public void Classifiers_preserve_unique_constraint_mapping()
    {
        Assert.AreEqual(
            DataCommandFailureKind.UniqueConstraint,
            DataCommandExceptionMapper.ClassifySqlServer(2601));
        Assert.AreEqual(
            DataCommandFailureKind.UniqueConstraint,
            DataCommandExceptionMapper.ClassifySqlServer(2627));
        Assert.AreEqual(
            DataCommandFailureKind.UniqueConstraint,
            DataCommandExceptionMapper.ClassifyMySql(MySqlErrorCode.DuplicateKeyEntry));
    }

    /// <summary>
    /// 验证未知 Provider 错误码不会被误分类为业务可处理错误。
    /// </summary>
    [TestMethod]
    public void Classifiers_ignore_unknown_errors()
    {
        Assert.IsNull(DataCommandExceptionMapper.ClassifySqlServer(0));
        Assert.IsNull(DataCommandExceptionMapper.ClassifyMySql(MySqlErrorCode.None));
    }
}
