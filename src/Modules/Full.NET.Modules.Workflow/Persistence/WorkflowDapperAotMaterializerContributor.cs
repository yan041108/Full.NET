#if FULLNET_AOT_COMPILE
using System.Data.Common;
using Full.NET.Data.Dapper;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>Workflow Native AOT 行物化器；读取顺序必须与 WorkflowSql 的显式投影保持一致。</summary>
internal sealed class WorkflowDapperAotMaterializerContributor
    : IDapperAotMaterializerContributor
{
    /// <summary>注册 Workflow 所有显式 SQL 投影对应的 Native AOT 行物化器。</summary>
    /// <param name="registrar">应用级 Dapper AOT 物化器注册表。</param>
    public void RegisterMaterializers(DapperAotMaterializerRegistrar registrar)
    {
        registrar.Register<WorkflowDefinitionRecord>(ReadDefinition);
        registrar.Register<WorkflowDefinitionDraftRecord>(ReadDefinitionDraft);
        registrar.Register<WorkflowDefinitionVersionRecord>(ReadDefinitionVersion);
        registrar.Register<WorkflowFormDefinitionRecord>(ReadFormDefinition);
        registrar.Register<WorkflowFormVersionRecord>(ReadFormVersion);
        registrar.Register<WorkflowInstanceRecord>(ReadInstance);
        registrar.Register<WorkflowTodoRecord>(ReadTodo);
        registrar.Register<WorkflowActiveWorkRecord>(ReadActiveWork);
        registrar.Register<WorkflowTodoRuntimeRecord>(ReadTodoRuntime);
        registrar.Register<WorkflowRuntimeAssetRecord>(ReadRuntimeAsset);
        registrar.Register<WorkflowFormSubmissionRecord>(ReadFormSubmission);
        registrar.Register<WorkflowActionReceiptRecord>(ReadActionReceipt);
        registrar.Register<WorkflowExecutionLogRecord>(ReadExecutionLog);
        registrar.Register<WorkflowCcRecord>(ReadCc);
    }

    private static WorkflowDefinitionRecord ReadDefinition(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            AotDataReaderExtensions.ReadNullableGuid(reader, 1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            AotDataReaderExtensions.ReadNullableGuid(reader, 5),
            AotDataReaderExtensions.ReadNullableGuid(reader, 6),
            reader.GetGuid(7),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 8),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 9),
            reader.GetInt64(10));

    private static WorkflowDefinitionDraftRecord ReadDefinitionDraft(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetInt64(3),
            reader.GetString(4),
            reader.GetGuid(5),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 6));

    private static WorkflowDefinitionVersionRecord ReadDefinitionVersion(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetGuid(7),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 8));

    private static WorkflowFormDefinitionRecord ReadFormDefinition(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            AotDataReaderExtensions.ReadNullableGuid(reader, 1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetInt64(6),
            AotDataReaderExtensions.ReadNullableGuid(reader, 7),
            reader.GetGuid(8),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 9),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 10));

    private static WorkflowFormVersionRecord ReadFormVersion(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetGuid(9),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 10));

    private static WorkflowInstanceRecord ReadInstance(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            AotDataReaderExtensions.ReadNullableGuid(reader, 1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetGuid(4),
            AotDataReaderExtensions.ReadNullableGuid(reader, 5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetInt64(9),
            reader.GetGuid(10),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 11),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 12),
            AotDataReaderExtensions.ReadNullableGuid(reader, 13),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 14),
            AotDataReaderExtensions.ReadNullableString(reader, 15),
            AotDataReaderExtensions.ReadNullableString(reader, 16),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 17));

    private static WorkflowTodoRecord ReadTodo(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            reader.GetString(4),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 5),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 6),
            AotDataReaderExtensions.ReadNullableString(reader, 7),
            reader.GetInt64(8));

    private static WorkflowActiveWorkRecord ReadActiveWork(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetInt64(1),
            reader.GetGuid(2),
            reader.GetInt64(3));

    private static WorkflowTodoRuntimeRecord ReadTodoRuntime(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            reader.GetString(4),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 5),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 6),
            AotDataReaderExtensions.ReadNullableString(reader, 7),
            reader.GetInt64(8),
            reader.GetString(9));

    private static WorkflowRuntimeAssetRecord ReadRuntimeAsset(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3));

    private static WorkflowFormSubmissionRecord ReadFormSubmission(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetInt64(5),
            reader.GetGuid(6),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 7));

    private static WorkflowActionReceiptRecord ReadActionReceipt(DbDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetGuid(1),
            reader.GetInt64(2),
            reader.GetString(3),
            AotDataReaderExtensions.ReadNullableString(reader, 4));

    private static WorkflowExecutionLogRecord ReadExecutionLog(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            AotDataReaderExtensions.ReadNullableGuid(reader, 2),
            reader.GetString(3),
            AotDataReaderExtensions.ReadNullableString(reader, 4),
            reader.GetString(5),
            AotDataReaderExtensions.ReadNullableString(reader, 6),
            AotDataReaderExtensions.ReadNullableString(reader, 7),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 8));

    /// <summary>按显式 SQL 投影顺序物化“我的抄送”记录。</summary>
    /// <param name="reader">定位到当前行的数据读取器。</param>
    /// <returns>Native AOT 安全的抄送持久化投影。</returns>
    private static WorkflowCcRecord ReadCc(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            AotDataReaderExtensions.ReadNullableGuid(reader, 2),
            reader.GetString(3),
            reader.GetGuid(4),
            reader.GetString(5),
            reader.GetString(6),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 7),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 8));
}
#endif
