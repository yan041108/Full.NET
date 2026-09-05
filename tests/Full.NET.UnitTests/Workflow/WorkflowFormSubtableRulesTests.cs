using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

[TestClass]
public sealed class WorkflowFormSubtableRulesTests
{
    [TestMethod]
    public void Validate_accepts_bounded_subtable_rows_with_valid_cells()
    {
        var schema = Schema(Field(
            "lineItems",
            "subtable",
            true,
            """
            {
              "maxRows": 5,
              "columns": [
                {
                  "columnKey": "itemName",
                  "fieldTypeKey": "text",
                  "required": true,
                  "constraints": { "minLength": 1, "maxLength": 32 }
                },
                {
                  "columnKey": "quantity",
                  "fieldTypeKey": "integer",
                  "required": true,
                  "constraints": { "minimum": 1, "maximum": 99 }
                }
              ]
            }
            """));
        var values = ParseObject("""
            {
              "lineItems": [
                { "itemName": "Widget", "quantity": 2 },
                { "itemName": "Gadget", "quantity": 1 }
              ]
            }
            """);

        Assert.IsTrue(WorkflowFormValueValidator.Validate(schema, values));
    }

    [TestMethod]
    public void Validate_rejects_subtable_rows_exceeding_max_rows_or_unknown_columns()
    {
        var schema = Schema(Field(
            "lineItems",
            "subtable",
            false,
            """
            {
              "maxRows": 1,
              "columns": [
                {
                  "columnKey": "itemName",
                  "fieldTypeKey": "text",
                  "required": true,
                  "constraints": { "minLength": 1, "maxLength": 32 }
                }
              ]
            }
            """));
        var tooMany = ParseObject("""
            {
              "lineItems": [
                { "itemName": "A" },
                { "itemName": "B" }
              ]
            }
            """);
        var unknownColumn = ParseObject("""
            {
              "lineItems": [
                { "itemName": "A", "extra": "x" }
              ]
            }
            """);

        Assert.IsFalse(WorkflowFormValueValidator.Validate(schema, tooMany));
        Assert.IsFalse(WorkflowFormValueValidator.Validate(schema, unknownColumn));
    }

    [TestMethod]
    public void Compile_rejects_nested_subtable_and_invalid_column_constraints()
    {
        var nested = Schema(Field(
            "lineItems",
            "subtable",
            false,
            """
            {
              "maxRows": 3,
              "columns": [
                {
                  "columnKey": "nested",
                  "fieldTypeKey": "subtable",
                  "required": false,
                  "constraints": { "maxRows": 1, "columns": [] }
                }
              ]
            }
            """));
        var missingChoiceOptions = Schema(Field(
            "lineItems",
            "subtable",
            false,
            """
            {
              "maxRows": 3,
              "columns": [
                {
                  "columnKey": "status",
                  "fieldTypeKey": "select",
                  "required": true,
                  "constraints": {}
                }
              ]
            }
            """));

        Assert.IsFalse(WorkflowFormCompiler.Compile(nested).IsSuccess);
        Assert.IsFalse(WorkflowFormCompiler.Compile(missingChoiceOptions).IsSuccess);
    }

    [TestMethod]
    public void TryApplyPatch_rejects_read_only_subtable_field_updates()
    {
        var schema = Schema(Field(
            "lineItems",
            "subtable",
            false,
            """
            {
              "maxRows": 3,
              "columns": [
                {
                  "columnKey": "itemName",
                  "fieldTypeKey": "text",
                  "required": true,
                  "constraints": { "minLength": 1, "maxLength": 32 }
                }
              ]
            }
            """));
        var compiled = WorkflowDefinitionCompiler.Compile(
            new WorkflowDefinitionDraft(1,
            [
                Node("start", "start", """{"nextNodeKeys":["approve"]}"""),
                Node("approve", "human.approval",
                    """{"nextNodeKeys":["end"],"fieldPolicies":{"lineItems":"readOnly"}}"""),
                Node("end", "end", "{}"),
            ]),
            schema);
        Assert.IsTrue(compiled.IsSuccess);
        Assert.IsTrue(WorkflowNodeFieldPolicy.TryResolve(
            compiled.Value!.CanonicalJson, "approve", schema, out var policy));

        var accepted = policy!.TryApplyPatch(
            schema,
            ParseObject("""{"lineItems":[{"itemName":"changed"}]}"""),
            ParseElement("""{"lineItems":[{"itemName":"changed"}]}"""),
            out _);

        Assert.IsFalse(accepted);
    }

    private static WorkflowFormSchema Schema(params WorkflowFormField[] fields) =>
        new(1, 1, [new WorkflowFormSection("main", fields)]);

    private static WorkflowFormField Field(
        string key,
        string type,
        bool required,
        string constraints) =>
        new(key, type, required, ParseObject(constraints));

    private static WorkflowNodeDraft Node(string key, string type, string json) =>
        new(key, type, 1, ParseElement(json));

    private static IReadOnlyDictionary<string, JsonElement> ParseObject(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
    }

    private static JsonElement ParseElement(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
