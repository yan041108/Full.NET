using Full.NET.Data.Abstractions;
using Full.NET.Modules.Auditing.Persistence;

namespace Full.NET.UnitTests.Auditing;

[TestClass]
public sealed class AuditingSqlScopeTests
{
    [TestMethod]
    public void Audit_writes_accept_host_tenant_and_anonymous_request_contexts()
    {
        Assert.AreEqual(SqlDataScope.Global, AccessLogSql.Insert.Scope);
        Assert.AreEqual(SqlDataScope.Global, OperationLogSql.Insert.Scope);
        Assert.AreEqual(SqlDataScope.Global, ExceptionLogSql.Insert.Scope);
    }

    [TestMethod]
    public void Audit_queries_remain_host_only()
    {
        Assert.AreEqual(SqlDataScope.HostOnly, AccessLogSql.FindById.Scope);
        Assert.AreEqual(SqlDataScope.HostOnly, OperationLogSql.FindById.Scope);
        Assert.AreEqual(SqlDataScope.HostOnly, ExceptionLogSql.FindById.Scope);
    }
}
