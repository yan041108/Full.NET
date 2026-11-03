using System.Text.Json;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Data.CodeGeneration.Schema;
using Full.NET.Modules.CodeGeneration.Contracts;
using Full.NET.Modules.CodeGeneration.Features.NormalizeCrudSchema;
using Full.NET.Modules.CodeGeneration.Serialization;
using Full.NET.Tests.CodeGeneration;

namespace Full.NET.UnitTests.CodeGeneration;

/// <summary>关系快照经过 HTTP 源生成序列化和规范化后，仍绑定相同生成输入。</summary>
[TestClass]
public sealed class GeneratedRemediationSchemaTransportTests
{
    [TestMethod]
    public void Relationship_snapshots_round_trip_and_change_the_review_hash()
    {
        var input = Request(CodeGenerationRemediationSchemas.Product());
        var json = JsonSerializer.Serialize(input, CodeGenerationJsonSerializerContext.Default.CodeGenerationPreviewRequest);
        var decoded = JsonSerializer.Deserialize(json, CodeGenerationJsonSerializerContext.Default.CodeGenerationPreviewRequest);
        Assert.IsNotNull(decoded);
        var normalizer = new CodeGenerationSchemaNormalizer();
        var first = normalizer.Normalize(decoded);
        Assert.IsTrue(first.IsSuccess);
        Assert.AreEqual(CrudArtifactGenerator.Generate(CodeGenerationRemediationSchemas.Product()).Count,
            CrudArtifactGenerator.Generate(first.Value!.Schema).Count);
        var relation = decoded.Relationships![0];
        var child = relation.DependentSchema!;
        var changed = decoded with { Relationships = [relation with { DependentSchema = child with
        {
            Columns = child.Columns.Select(column => column.DatabaseName == "Description" ? column with { MaxLength = 32 } : column).ToArray(),
        } }] };
        var second = normalizer.Normalize(changed);
        Assert.IsTrue(second.IsSuccess);
        Assert.AreNotEqual(first.Value.SchemaSha256, second.Value!.SchemaSha256);
    }

    [TestMethod]
    public void Nested_relationship_snapshots_are_rejected_without_recursing()
    {
        var input = Request(CodeGenerationRemediationSchemas.Product());
        var relation = input.Relationships![0];
        var nested = input with { Relationships = [relation with { DependentSchema = input }] };
        Assert.IsFalse(new CodeGenerationSchemaNormalizer().Normalize(nested).IsSuccess);
    }

    private static CodeGenerationPreviewRequest Request(FullNetCrudSchema schema) => new(
        schema.OwnerKey, schema.ModuleKey, schema.EntityKey, schema.DatabaseTableName, schema.RootNamespace,
        schema.ClrTypeName, schema.ApiResourceName, schema.PermissionResourceName,
        FullNetCrudWireValues.ToWireValue(schema.DataScope), null,
        schema.Columns.Select(column => new CodeGenerationPreviewColumnRequest(column.DatabaseName, column.ClrPropertyName,
            column.JsonPropertyName, FullNetCrudWireValues.ToWireValue(column.ScalarType), column.IsNullable, column.MaxLength,
            column.NumericPrecision, column.NumericScale, column.Ui is null ? null : new(
                FullNetCrudWireValues.ToWireValue(column.Ui.ControlKind), column.Ui.ShowInList, column.Ui.IncludeInCreate,
                column.Ui.IncludeInUpdate, column.Ui.Required, column.Ui.Sortable, column.Ui.Queryable,
                FullNetCrudWireValues.ToWireValue(column.Ui.QueryKind), column.Ui.Unique, column.Ui.IncludeInImportExport))).ToArray(),
        new(FullNetCrudWireValues.ToWireValue(schema.EntityCapabilities.DeleteMode), schema.EntityCapabilities.HasCreatedAudit,
            schema.EntityCapabilities.HasUpdatedAudit, schema.EntityCapabilities.HasDeletedAudit, schema.HasVersion,
            FullNetCrudWireValues.ToWireValue(schema.EntityCapabilities.OwnershipMode)),
        FullNetCrudWireValues.ToWireValue(schema.Scene), schema.Relationships.Select(relation => new CodeGenerationRelationshipRequest(
            relation.PrincipalEntityKey, relation.PrincipalColumnName, FullNetCrudWireValues.ToWireValue(relation.PrincipalDataScope),
            relation.DependentEntityKey, relation.DependentColumnName, FullNetCrudWireValues.ToWireValue(relation.DependentDataScope),
            relation.CompositeKeyColumnNames, relation.CascadeDelete,
            relation.PrincipalSchema is null ? null : Request(relation.PrincipalSchema),
            relation.DependentSchema is null ? null : Request(relation.DependentSchema))).ToArray());
}
