namespace Full.NET.Migrations.DbUp;

public sealed record MigrationResult(bool Successful, int ExecutedScriptCount);

public interface IDatabaseMigrationRunner
{
    Task<MigrationResult> MigrateAsync(CancellationToken cancellationToken = default);
}
