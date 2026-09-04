using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

/// <summary>验证排他网关只接受闭合条件，并按稳定顺序选择唯一分支。</summary>
[TestClass]
public sealed class WorkflowExclusiveGatewayConfigurationTests
{
    /// <summary>验证多个条件同时成立时选择第一个分支，其余情况按条件或默认分支收敛。</summary>
    [TestMethod]
    public void Selects_first_matching_branch_and_falls_back_to_default()
    {
        var schema = Schema(
            Field("amount", "money", required: true, "{\"scale\":2}"),
            Field("urgent", "switch", required: true, "{}"));
        var config = ParseElement("""
            {
              "nodeName": "金额分流",
              "nextNodeKeys": ["finance", "urgent-review", "manager"],
              "branches": [
                {
                  "branchKey": "large-amount",
                  "nextNodeKey": "finance",
                  "condition": {
                    "fieldKey": "amount",
                    "operator": "greaterThanOrEqual",
                    "value": "1000.00"
                  }
                },
                {
                  "branchKey": "urgent",
                  "nextNodeKey": "urgent-review",
                  "condition": {
                    "fieldKey": "urgent",
                    "operator": "equals",
                    "value": true
                  }
                }
              ],
              "defaultNextNodeKey": "manager"
            }
            """);

        Assert.IsTrue(WorkflowExclusiveGatewayConfiguration.TryRead(config, schema, out var gateway));
        Assert.IsNotNull(gateway);

        Assert.IsTrue(gateway.TrySelectBranch(
            Values("{\"amount\":\"1200.00\",\"urgent\":true}"), out var large));
        Assert.AreEqual("large-amount", large.BranchKey);
        Assert.AreEqual("finance", large.NextNodeKey);

        Assert.IsTrue(gateway.TrySelectBranch(
            Values("{\"amount\":\"100.00\",\"urgent\":true}"), out var urgent));
        Assert.AreEqual("urgent", urgent.BranchKey);
        Assert.AreEqual("urgent-review", urgent.NextNodeKey);

        Assert.IsTrue(gateway.TrySelectBranch(
            Values("{\"amount\":\"100.00\",\"urgent\":false}"), out var fallback));
        Assert.AreEqual("default", fallback.BranchKey);
        Assert.AreEqual("manager", fallback.NextNodeKey);
    }

    /// <summary>验证可选字段缺失或为空时只由显式空值操作符处理。</summary>
    [TestMethod]
    public void Empty_operators_handle_missing_optional_fields_without_coercion()
    {
        var schema = Schema(Field("reason", "text", required: false,
            "{\"minLength\":1,\"maxLength\":128}"));
        var config = ParseElement("""
            {
              "nextNodeKeys": ["without-reason", "with-reason", "end"],
              "branches": [
                {
                  "branchKey": "missing",
                  "nextNodeKey": "without-reason",
                  "condition": { "fieldKey": "reason", "operator": "isEmpty" }
                },
                {
                  "branchKey": "present",
                  "nextNodeKey": "with-reason",
                  "condition": { "fieldKey": "reason", "operator": "isNotEmpty" }
                }
              ],
              "defaultNextNodeKey": "end"
            }
            """);

        Assert.IsTrue(WorkflowExclusiveGatewayConfiguration.TryRead(config, schema, out var gateway));
        Assert.IsTrue(gateway!.TrySelectBranch(Values("{}"), out var missing));
        Assert.AreEqual("missing", missing.BranchKey);
        Assert.IsTrue(gateway.TrySelectBranch(Values("{\"reason\":\"说明\"}"), out var present));
        Assert.AreEqual("present", present.BranchKey);
    }

    /// <summary>验证发布期拒绝未知字段、类型不兼容操作符与非规范条件值。</summary>
    /// <param name="conditionJson">待验证的条件 JSON。</param>
    [TestMethod]
    [DataRow("{\"fieldKey\":\"missing\",\"operator\":\"equals\",\"value\":1}")]
    [DataRow("{\"fieldKey\":\"title\",\"operator\":\"greaterThan\",\"value\":\"b\"}")]
    [DataRow("{\"fieldKey\":\"amount\",\"operator\":\"greaterThan\",\"value\":100}")]
    [DataRow("{\"fieldKey\":\"amount\",\"operator\":\"eval\",\"value\":\"100.00\"}")]
    [DataRow("{\"fieldKey\":\"amount\",\"operator\":\"isEmpty\",\"value\":\"100.00\"}")]
    public void Rejects_unknown_fields_incompatible_operators_and_noncanonical_values(
        string conditionJson)
    {
        var schema = Schema(
            Field("title", "text", required: false, "{\"minLength\":1,\"maxLength\":32}"),
            Field("amount", "money", required: false, "{\"scale\":2}"));
        var config = GatewayConfig(conditionJson);

        Assert.IsFalse(WorkflowExclusiveGatewayConfiguration.TryRead(config, schema, out _));
    }

    /// <summary>验证结构层拒绝未知字段、重复键、缺失默认分支与目标集合漂移。</summary>
    /// <param name="configJson">待验证的完整网关配置 JSON。</param>
    [TestMethod]
    [DataRow("{\"nextNodeKeys\":[\"yes\",\"no\"],\"branches\":[{\"branchKey\":\"yes\",\"nextNodeKey\":\"yes\",\"condition\":{\"fieldKey\":\"flag\",\"operator\":\"equals\",\"value\":true}}]}")]
    [DataRow("{\"nextNodeKeys\":[\"yes\",\"no\"],\"branches\":[{\"branchKey\":\"same\",\"nextNodeKey\":\"yes\",\"condition\":{\"fieldKey\":\"flag\",\"operator\":\"equals\",\"value\":true}},{\"branchKey\":\"same\",\"nextNodeKey\":\"no\",\"condition\":{\"fieldKey\":\"flag\",\"operator\":\"equals\",\"value\":false}}],\"defaultNextNodeKey\":\"no\"}")]
    [DataRow("{\"nextNodeKeys\":[\"yes\",\"other\"],\"branches\":[{\"branchKey\":\"yes\",\"nextNodeKey\":\"yes\",\"condition\":{\"fieldKey\":\"flag\",\"operator\":\"equals\",\"value\":true}}],\"defaultNextNodeKey\":\"no\"}")]
    [DataRow("{\"nextNodeKeys\":[\"yes\",\"no\"],\"branches\":[{\"branchKey\":\"yes\",\"nextNodeKey\":\"yes\",\"condition\":{\"fieldKey\":\"flag\",\"operator\":\"equals\",\"value\":true}}],\"defaultNextNodeKey\":\"no\",\"script\":\"return true\"}")]
    [DataRow("{\"nextNodeKeys\":[\"yes\",\"no\"],\"branches\":[{\"branchKey\":\"yes\",\"nextNodeKey\":\"yes\",\"condition\":{\"fieldKey\":\"flag\",\"operator\":\"equals\",\"value\":true,\"remoteUrl\":\"https://example.test\"}}],\"defaultNextNodeKey\":\"no\"}")]
    public void Rejects_malformed_or_open_ended_configuration(string configJson)
    {
        var schema = Schema(Field("flag", "switch", required: true, "{}"));

        Assert.IsFalse(WorkflowExclusiveGatewayConfiguration.TryRead(
            ParseElement(configJson), schema, out _));
    }

    /// <summary>验证没有表单架构时只校验闭合结构，供图级编译先完成拓扑验证。</summary>
    [TestMethod]
    public void Structural_read_accepts_closed_shape_without_form_schema()
    {
        var config = GatewayConfig(
            "{\"fieldKey\":\"amount\",\"operator\":\"greaterThan\",\"value\":\"100.00\"}");

        Assert.IsTrue(WorkflowExclusiveGatewayConfiguration.TryRead(config, null, out var gateway));
        Assert.IsNotNull(gateway);
    }

    private static JsonElement GatewayConfig(string conditionJson) => ParseElement($$"""
        {
          "nextNodeKeys": ["yes", "no"],
          "branches": [{
            "branchKey": "yes",
            "nextNodeKey": "yes",
            "condition": {{conditionJson}}
          }],
          "defaultNextNodeKey": "no"
        }
        """);

    private static WorkflowFormSchema Schema(params WorkflowFormField[] fields) =>
        new(1, 1, [new WorkflowFormSection("main", fields)]);

    private static WorkflowFormField Field(
        string key,
        string type,
        bool required,
        string constraints) =>
        new(key, type, required, ParseObject(constraints));

    private static IReadOnlyDictionary<string, JsonElement> Values(string json) =>
        ParseObject(json);

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
