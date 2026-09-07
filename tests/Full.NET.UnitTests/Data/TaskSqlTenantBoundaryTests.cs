using System.Reflection;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.ImportExport.Persistence;
using Full.NET.Modules.Reporting.Persistence;

namespace Full.NET.UnitTests.Data;

/// <summary>导入与导出任务的业务 SQL 必须通过真实租户守卫并由可信上下文绑定。</summary>
[TestClass]
public sealed class TaskSqlTenantBoundaryTests
{
    /// <summary>覆盖所有静态任务 SQL，新增调度目录只允许返回租户标识。</summary>
    /// <returns>携带模块和声明名的测试输入。</returns>
    public static IEnumerable<object[]> Statements() =>
        new[] { typeof(ImportExportTaskSql), typeof(ReportingExportTaskSql) }
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(field => field.FieldType == typeof(SqlStatement))
            .Select(field => (SqlStatement)field.GetValue(null)!)
            .Where(statement => statement.Scope == SqlDataScope.TenantRequired)
            .Select(statement => new object[] { statement.Name, statement });

    /// <summary>任务 SQL 接受可信租户，缺少上下文仍失败关闭。</summary>
    /// <param name="name">语句标识。</param>
    /// <param name="statement">真实生产 SQL。</param>
    [TestMethod]
    [DynamicData(nameof(Statements))]
    public void Business_statement_requires_current_tenant(string name, SqlStatement statement)
    {
        var tenant = new CurrentTenantAccessor();
        tenant.SetTenant(new TenantContext(Guid.NewGuid(), "tenant-a", "租户 A"));
        SqlScopeGuard.Validate(statement, tenant);
        Assert.AreEqual(SqlTenantBinding.CurrentTenantId, statement.TenantBinding, name);
        Assert.Throws<TenantContextMissingException>(() => SqlScopeGuard.Validate(statement, new CurrentTenantAccessor()));
    }
}
