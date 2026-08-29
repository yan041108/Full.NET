#if FULLNET_AOT_COMPILE
using System.Data.Common;
using Full.NET.Data.Dapper;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>Workflow Native AOT 行物化器；读取顺序必须与 WorkflowSql 的显式投影保持一致。</summary>
internal sealed class WorkflowDapperAotMaterializerContributor
    : IDapperAotMaterializerContributor
{
    public void RegisterMaterializers(DapperAotMaterializerRegistrar registrar)
    {
        registrar.Register<WorkflowDefinitionRecord>(ReadDefinition);
        registrar.Register<WorkflowDefinitionDraftRecord>(ReadDefinitionDraft);
        registrar.Register<WorkflowDefinitionVersionRecord>(ReadDefinitionVersion);
        registrar.Register<WorkflowFormDefinitionRecord>(ReadFormDefinition);
        registrar.Register<WorkflowFormVersionRecord>(ReadFormVersion);
        registrar.Register<WorkflowInstanceRecord>(ReadInstance);
        registrar.Register<WorkflowTodoRecord>(ReadTodo);
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
}
#endif
