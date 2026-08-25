using System.Data;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using global::Dapper;
using Microsoft.Data.SqlClient;

namespace Full.NET.UnitTests.Data;

/// <summary>
/// 验证静态 Plan 只有显式登记的固定参数顺序可进入，并按 Provider 隔离命令槽。
/// </summary>
[TestClass]
public sealed class DapperAotStaticCommandPlanRegistryTests
{
    [TestMethod]
    public async Task RegisteredPlan_ReusesCommandWithoutRuntimeShapeDiscovery()
    {
        var statementName = $"test.static-plan.{Guid.NewGuid():N}";
        DapperAotStaticCommandPlanRegistry.Register(
            statementName,
            ["Id", "Name"]);
        Assert.IsTrue(DapperAotStaticCommandPlanRegistry.TryGetFactory(
            statementName,
            DatabaseProvider.SqlServer,
            out var factory));
        var firstParameters = CreateParameters(1, "first");
        await using var firstConnection = new SqlConnection();
        var firstCommand = factory.GetCommand(
            firstConnection,
            "SELECT @Id, @Name",
            CommandType.Text,
            firstParameters);
        var firstId = firstCommand.Parameters[0];
        Assert.IsTrue(factory.TryRecycle(firstCommand));

        var secondParameters = CreateParameters(2, "second");
        await using var secondConnection = new SqlConnection();
        var secondCommand = factory.GetCommand(
            secondConnection,
            "SELECT @Id, @Name",
            CommandType.Text,
            secondParameters);
        secondCommand.Connection = secondConnection;

        Assert.AreSame(firstCommand, secondCommand);
        Assert.AreSame(firstId, secondCommand.Parameters[0]);
        Assert.AreEqual(2, secondCommand.Parameters[0].Value);
        Assert.AreEqual("second", secondCommand.Parameters[1].Value);
        secondCommand.Dispose();
    }

    [TestMethod]
    public void Register_IsIdempotentForSameShapeAndRejectsConflictingShape()
    {
        var statementName = $"test.static-plan.{Guid.NewGuid():N}";
        DapperAotStaticCommandPlanRegistry.Register(statementName, ["Id"]);
        DapperAotStaticCommandPlanRegistry.Register(statementName, ["Id"]);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            DapperAotStaticCommandPlanRegistry.Register(
                statementName,
                ["Different"]));
    }

    [TestMethod]
    public void TryGetFactory_IsolatesProviderSlotsAndUnknownStatements()
    {
        var statementName = $"test.static-plan.{Guid.NewGuid():N}";
        DapperAotStaticCommandPlanRegistry.Register(statementName, ["Id"]);

        Assert.IsTrue(DapperAotStaticCommandPlanRegistry.TryGetFactory(
            statementName,
            DatabaseProvider.SqlServer,
            out var sqlServerFactory));
        Assert.IsTrue(DapperAotStaticCommandPlanRegistry.TryGetFactory(
            statementName,
            DatabaseProvider.MySql,
            out var mySqlFactory));
        Assert.AreNotSame(sqlServerFactory, mySqlFactory);
        Assert.IsFalse(DapperAotStaticCommandPlanRegistry.TryGetFactory(
            statementName + ".missing",
            DatabaseProvider.SqlServer,
            out _));
    }

    [TestMethod]
    public async Task RegisteredPlan_NeverSharesAnInUseCommandAndCachesOnlyOneIdleCommand()
    {
        var statementName = $"test.static-plan.{Guid.NewGuid():N}";
        DapperAotStaticCommandPlanRegistry.Register(statementName, ["Id", "Name"]);
        Assert.IsTrue(DapperAotStaticCommandPlanRegistry.TryGetFactory(
            statementName,
            DatabaseProvider.SqlServer,
            out var factory));
        await using var firstConnection = new SqlConnection();
        await using var secondConnection = new SqlConnection();
        var firstCommand = factory.GetCommand(
            firstConnection,
            "SELECT @Id, @Name",
            CommandType.Text,
            CreateParameters(1, "first"));
        var secondCommand = factory.GetCommand(
            secondConnection,
            "SELECT @Id, @Name",
            CommandType.Text,
            CreateParameters(2, "second"));

        Assert.AreNotSame(firstCommand, secondCommand);
        Assert.IsTrue(factory.TryRecycle(firstCommand));
        Assert.IsFalse(factory.TryRecycle(secondCommand));
        secondCommand.Dispose();
    }

    private static DynamicParameters CreateParameters(int id, string name)
    {
        var parameters = new DynamicParameters();
        parameters.Add("Id", id);
        parameters.Add("Name", name);
        return parameters;
    }
}
