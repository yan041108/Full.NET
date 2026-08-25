#if FULLNET_AOT_COMPILE
using System.Data.Common;
using System.Globalization;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Jobs.Execution;
using Full.NET.Modules.Jobs.Features.ManageHostJobHealth;

namespace Full.NET.Modules.Jobs.Persistence;

/// <summary>
/// Jobs Native AOT 行物化器。Host.Api 触发会在进程内领取执行，列表与领取投影必须共用同一序数读取器。
/// </summary>
internal sealed class JobsDapperAotMaterializerContributor : IDapperAotMaterializerContributor
{
    public void RegisterMaterializers(DapperAotMaterializerRegistrar registrar)
    {
        registrar.Register<JobDefinitionRecord>(ReadJobDefinitionRecord);
        registrar.Register<JobExecutionRecord>(ReadJobExecutionRecord);
        registrar.Register<JobDefinitionOptionRecord>(ReadJobDefinitionOptionRecord);
        registrar.Register<JobScheduleRecord>(ReadJobScheduleRecord);
        registrar.Register<JobScheduleDetailRecord>(ReadJobScheduleDetailRecord);
        registrar.Register<JobWorkerInstanceRecord>(ReadJobWorkerInstanceRecord);
        registrar.Register<JobsBacklogSqlServerRow>(ReadJobsBacklogSqlServerRow);
        registrar.Register<JobsBacklogMySqlRow>(ReadJobsBacklogMySqlRow);
    }

    private static JobDefinitionRecord ReadJobDefinitionRecord(DbDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            TenantId = AotDataReaderExtensions.ReadNullableGuid(reader, 1),
            JobKey = reader.GetString(2),
            HandlerKind = reader.GetString(3),
            ArgsJson = AotDataReaderExtensions.ReadNullableString(reader, 4),
            DisplayName = reader.GetString(5),
            Description = AotDataReaderExtensions.ReadNullableString(reader, 6),
            GroupName = AotDataReaderExtensions.ReadNullableString(reader, 7),
            IsEnabled = AotDataReaderExtensions.ReadBoolean(reader, 8),
            AllowConcurrentExecutions = AotDataReaderExtensions.ReadBoolean(reader, 9),
            CreatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 10),
            UpdatedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 11),
            CreatedByUserId = reader.GetGuid(12),
            UpdatedByUserId = AotDataReaderExtensions.ReadNullableGuid(reader, 13),
            Version = reader.GetInt32(14),
        };

    private static JobExecutionRecord ReadJobExecutionRecord(DbDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            TenantId = AotDataReaderExtensions.ReadNullableGuid(reader, 1),
            JobDefinitionId = reader.GetGuid(2),
            JobScheduleId = AotDataReaderExtensions.ReadNullableGuid(reader, 3),
            Status = reader.GetString(4),
            TriggerKind = reader.GetString(5),
            ScheduledForUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 6),
            ErrorMessage = AotDataReaderExtensions.ReadNullableString(reader, 7),
            StartedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 8),
            FinishedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 9),
            LeaseId = AotDataReaderExtensions.ReadNullableGuid(reader, 10),
            LeaseExpiresAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 11),
            NextAttemptAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 12),
            AttemptCount = reader.GetInt32(13),
            CreatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 14),
            JobKey = reader.IsDBNull(15) ? string.Empty : reader.GetString(15),
        };

    private static JobDefinitionOptionRecord ReadJobDefinitionOptionRecord(DbDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            JobKey = reader.GetString(1),
            HandlerKind = reader.GetString(2),
            DisplayName = reader.GetString(3),
        };

    private static JobScheduleRecord ReadJobScheduleRecord(DbDataReader reader)
    {
        var record = ReadJobScheduleCore(reader);
        record.AllowConcurrentExecutions = AotDataReaderExtensions.ReadBoolean(reader, 22);
        return record;
    }

    private static JobScheduleDetailRecord ReadJobScheduleDetailRecord(DbDataReader reader)
    {
        var record = new JobScheduleDetailRecord
        {
            Id = reader.GetGuid(0),
            TenantId = AotDataReaderExtensions.ReadNullableGuid(reader, 1),
            JobDefinitionId = reader.GetGuid(2),
            TriggerKind = reader.GetString(3),
            CronExpression = AotDataReaderExtensions.ReadNullableString(reader, 4),
            TimeZoneId = reader.GetString(5),
            OneTimeAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 6),
            MisfirePolicy = reader.GetString(7),
            IsEnabled = AotDataReaderExtensions.ReadBoolean(reader, 8),
            NextExecutionAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 9),
            LastExecutionAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 10),
            CompletedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 11),
            NumberOfRuns = ReadInt64(reader, 12),
            NumberOfErrors = ReadInt64(reader, 13),
            StartTime = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 14),
            EndTime = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 15),
            Args = AotDataReaderExtensions.ReadNullableString(reader, 16),
            CreatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 17),
            CreatedByUserId = reader.GetGuid(18),
            UpdatedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 19),
            UpdatedByUserId = AotDataReaderExtensions.ReadNullableGuid(reader, 20),
            Version = reader.GetInt32(21),
            JobDefinitionJobKey = reader.GetString(22),
            JobDefinitionDisplayName = reader.GetString(23),
        };
        return record;
    }

    private static JobScheduleRecord ReadJobScheduleCore(DbDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            TenantId = AotDataReaderExtensions.ReadNullableGuid(reader, 1),
            JobDefinitionId = reader.GetGuid(2),
            TriggerKind = reader.GetString(3),
            CronExpression = AotDataReaderExtensions.ReadNullableString(reader, 4),
            TimeZoneId = reader.GetString(5),
            OneTimeAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 6),
            MisfirePolicy = reader.GetString(7),
            IsEnabled = AotDataReaderExtensions.ReadBoolean(reader, 8),
            NextExecutionAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 9),
            LastExecutionAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 10),
            CompletedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 11),
            NumberOfRuns = ReadInt64(reader, 12),
            NumberOfErrors = ReadInt64(reader, 13),
            StartTime = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 14),
            EndTime = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 15),
            Args = AotDataReaderExtensions.ReadNullableString(reader, 16),
            CreatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 17),
            CreatedByUserId = reader.GetGuid(18),
            UpdatedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 19),
            UpdatedByUserId = AotDataReaderExtensions.ReadNullableGuid(reader, 20),
            Version = reader.GetInt32(21),
        };

    private static JobWorkerInstanceRecord ReadJobWorkerInstanceRecord(DbDataReader reader) =>
        new()
        {
            InstanceId = reader.GetGuid(0),
            HostProfile = reader.GetString(1),
            StartedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 2),
            LastHeartbeatAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 3),
            WorkerVersion = AotDataReaderExtensions.ReadNullableString(reader, 4),
        };

    private static JobsBacklogSqlServerRow ReadJobsBacklogSqlServerRow(DbDataReader reader) =>
        new()
        {
            PendingCount = ReadInt64(reader, 0),
            OldestClaimableCreatedAtUtc =
                AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 1),
            DueRetryCount = ReadInt64(reader, 2),
            OldestDueRetryAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 3),
        };

    private static JobsBacklogMySqlRow ReadJobsBacklogMySqlRow(DbDataReader reader) =>
        new()
        {
            PendingCount = ReadInt64(reader, 0),
            OldestClaimableCreatedAtUtc = ReadNullableUtcDateTime(reader, 1),
            DueRetryCount = ReadInt64(reader, 2),
            OldestDueRetryAtUtc = ReadNullableUtcDateTime(reader, 3),
        };

    private static long ReadInt64(DbDataReader reader, int ordinal) =>
        Convert.ToInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture);

    private static DateTime? ReadNullableUtcDateTime(DbDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        if (reader.GetFieldType(ordinal) == typeof(DateTimeOffset))
        {
            return reader.GetFieldValue<DateTimeOffset>(ordinal).UtcDateTime;
        }

        return DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc);
    }
}
#endif
