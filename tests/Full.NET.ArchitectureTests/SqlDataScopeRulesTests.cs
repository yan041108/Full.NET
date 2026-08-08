using System.Reflection;
using Full.NET.Data.Abstractions;

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
