using Microsoft.Extensions.Options;
using Full.NET.Migrations.DbUp;

namespace Full.NET.IntegrationTests.Migrations;

internal static class MigrationContractOptionFactory
{
    public const string UuidApprovalId = "test-uuid-contract-009";
    public const string NamingApprovalId = "test-pre-v1-naming-contract-011";

    public static IOptions<UuidBinaryContractOptions> UuidOptions() =>
        Options.Create(new UuidBinaryContractOptions
        {
            MaintenanceMode = true,
            BackupVerified = true,
            LegacyWritersStopped = true,
            DestructiveDdlApprovalId = UuidApprovalId,
        });

    public static IOptions<PreV1NamingContractOptions> NamingOptions() =>
        Options.Create(new PreV1NamingContractOptions
        {
            MaintenanceMode = true,
            BackupVerified = true,
            LegacyWritersStopped = true,
            LegacyOutboxDrained = true,
            DestructiveDdlApprovalId = NamingApprovalId,
        });
}
