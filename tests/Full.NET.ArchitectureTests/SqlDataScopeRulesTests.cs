using System.Reflection;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;

namespace Full.NET.ArchitectureTests;

[TestClass]
public sealed class SqlDataScopeRulesTests
{
    private static readonly Assembly[] SqlStatementAssemblies =
    [
        .. ProductionAssemblies.All,
        typeof(Full.NET.Modules.Jobs.JobsModule).Assembly,
        typeof(Full.NET.Modules.Messaging.MessagingModule).Assembly,
        typeof(Full.NET.Modules.Notifications.NotificationsModule).Assembly,
    ];

    [TestMethod]
    public void Production_statements_declare_tenant_binding_consistent_with_scope()
    {
        var offenders = SqlStatementAssemblies
            .Distinct()
            .SelectMany(GetLoadableTypes)
            .SelectMany(ReadSqlStatements)
            .Where(item => item.Statement.TenantBinding != ExpectedBinding(item.Statement.Scope))
            .Select(item =>
                $"{item.Location}: {item.Statement.Name} uses {item.Statement.Scope}/{item.Statement.TenantBinding}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.HasCount(0, offenders, string.Join(Environment.NewLine, offenders));
    }

    [TestMethod]
    public void Production_tenant_statements_use_tenant_parameter_in_a_safe_clause()
    {
        var currentTenant = new CurrentTenantAccessor();
        currentTenant.SetTenant(new TenantContext(
            Guid.Parse("0199382f-f88d-7000-8000-000000000002"),
            "architecture-test",
            "Architecture Test"));
        var offenders = SqlStatementAssemblies
            .Distinct()
            .SelectMany(GetLoadableTypes)
            .SelectMany(ReadSqlStatements)
            .Where(item => item.Statement.Scope == SqlDataScope.TenantRequired)
            .Select(item => new
            {
                item.Location,
                Error = ValidateTenantStatement(item.Statement, currentTenant),
            })
            .Where(item => item.Error is not null)
            .Select(item => $"{item.Location}: {item.Error}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.HasCount(0, offenders, string.Join(Environment.NewLine, offenders));
    }

    private static string? ValidateTenantStatement(
        SqlStatement statement,
        ICurrentTenant currentTenant)
    {
        try
        {
            SqlScopeGuard.Validate(statement, currentTenant);
            return null;
        }
        catch (TenantScopeViolationException exception)
        {
            return exception.Message;
        }
    }

    private static SqlTenantBinding ExpectedBinding(SqlDataScope scope) =>
        scope switch
        {
            SqlDataScope.TenantRequired => SqlTenantBinding.CurrentTenantId,
            SqlDataScope.Global or SqlDataScope.HostOnly => SqlTenantBinding.None,
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown SQL data scope."),
        };

    private static IEnumerable<StatementDeclaration> ReadSqlStatements(Type type)
    {
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        foreach (var field in type.GetFields(flags)
                     .Where(field => field.FieldType == typeof(SqlStatement)))
        {
            if (field.GetValue(null) is SqlStatement statement)
            {
                yield return new StatementDeclaration(
                    $"{type.FullName}.{field.Name}",
                    statement);
            }
        }

        foreach (var property in type.GetProperties(flags)
                     .Where(property => property.PropertyType == typeof(SqlStatement)
                         && property.GetIndexParameters().Length == 0))
        {
            if (property.GetValue(null) is SqlStatement statement)
            {
                yield return new StatementDeclaration(
                    $"{type.FullName}.{property.Name}",
                    statement);
            }
        }
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
    }

    private sealed record StatementDeclaration(string Location, SqlStatement Statement);
}
