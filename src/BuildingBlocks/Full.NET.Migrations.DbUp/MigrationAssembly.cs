using System.Reflection;

namespace Full.NET.Migrations.DbUp;

internal static class MigrationAssembly
{
    public static Assembly Value { get; } = typeof(MigrationAssembly).Assembly;
}
