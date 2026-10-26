using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;
namespace Full.NET.UnitTests.Workflow;

/// <summary>验证完整设计保存与业务投影的边界，不能以业务目录裁剪原版 JSON。</summary>
[TestClass]
public sealed class WorkflowDesignerAssetTests
{
    [TestMethod]
    public void Save_accepts_full_original_configuration_and_hashes_all_extensions()
    {
        using var document = JsonDocument.Parse("""{"widgetList":[{"id":"file1","type":"file-upload","options":{"name":"files","onChange":"return 1;","label":"附件","defaultValue":null}}],"formConfig":{"jsonVersion":3,"labelWidth":180,"cssCode":".a { color: red; }"},"extension":{"a":[1,true,null]}}""");
        Assert.IsTrue(WorkflowDesignerAsset.IsValid(document.RootElement));
        var artifact = WorkflowJsonCanonicalizer.Compile(document.RootElement);
        using var stored = JsonDocument.Parse(artifact.CanonicalJson);
        Assert.IsTrue(JsonElement.DeepEquals(document.RootElement, stored.RootElement));
        Assert.AreEqual(64, artifact.ContentHash.Length);
        Assert.IsNull(WorkflowDesignerAsset.Project(document.RootElement, out var diagnostic));
        StringAssert.Contains(diagnostic, "widgetList[0]");
    }
    [TestMethod]
    [DataRow("{\"widgetList\":[],\"formConfig\":{\"jsonVersion\":4}}")]
    [DataRow("{\"widgetList\":[],\"formConfig\":{},\"__proto__\":{}}")]
    [DataRow("{\"widgetList\":[],\"formConfig\":{},\"formConfig\":{}}")]
    [DataRow("{\"widgetList\":[1],\"formConfig\":{}}")]
    [DataRow("{\"widgetList\":[{\"options\":{}}],\"formConfig\":{}}")]
    [DataRow("{\"widgetList\":[{\"type\":\"grid\",\"options\":{},\"cols\":{}}],\"formConfig\":{}}")]
    public void Save_rejects_future_format_and_ambiguous_objects(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.IsFalse(WorkflowDesignerAsset.IsValid(document.RootElement));
    }
    [TestMethod]
    public void Publish_walks_nested_layout_and_keeps_label_separate_from_business_key()
    {
        using var document = JsonDocument.Parse("""{"widgetList":[{"type":"grid","category":"container","options":{"name":"grid"},"cols":[{"type":"grid-col","category":"container","options":{"name":"col"},"widgetList":[{"id":"title1","type":"input","options":{"name":"title","label":"申请标题","required":true,"placeholder":"填写标题"}}]}]}],"formConfig":{"jsonVersion":3}}""");
        var schema = WorkflowDesignerAsset.Project(document.RootElement, out var diagnostic);
        Assert.IsNull(diagnostic); Assert.IsNotNull(schema);
        Assert.AreEqual("title", schema.Sections[0].Fields[0].FieldKey);
        Assert.IsTrue(schema.Sections[0].Fields[0].Required);
        StringAssert.Contains(document.RootElement.GetRawText(), "申请标题");
    }
    [TestMethod]
    public void Runtime_view_removes_hidden_defaults_even_without_form_item_flag()
    {
        const string json = """{"widgetList":[{"type":"input","options":{"name":"visible","label":"可见","defaultValue":"old"}},{"type":"input","options":{"name":"secret","defaultValue":"never-send"}}],"formConfig":{"jsonVersion":3},"privateRoot":{"secret":"never-send"}}""";
        var view = WorkflowDesignerAsset.CreateRuntimeView(json, new Dictionary<string, string> { ["visible"] = "readOnly" });
        Assert.IsNotNull(view);
        Assert.IsFalse(view.Value.GetRawText().Contains("never-send", StringComparison.Ordinal));
        Assert.IsFalse(view.Value.GetProperty("widgetList")[0].GetProperty("options").TryGetProperty("defaultValue", out _));
        Assert.IsTrue(view.Value.GetProperty("widgetList")[0].GetProperty("options").GetProperty("disabled").GetBoolean());
    }
    [TestMethod]
    public void Runtime_view_rejects_script_with_dependencies_on_hidden_fields()
    {
        const string json = """{"widgetList":[{"type":"input","options":{"name":"secret","defaultValue":"never-send"}}],"formConfig":{"jsonVersion":3,"onFormMounted":"console.log(this.getFormData())"}}""";
        Assert.IsNull(WorkflowDesignerAsset.CreateRuntimeView(json, new Dictionary<string, string>()));
    }

    [TestMethod]
    public void Save_accepts_original_table_rows_without_widget_type()
    {
        using var document = JsonDocument.Parse("""{"widgetList":[{"type":"table","category":"container","options":{},"rows":[{"cols":[{"type":"table-cell","category":"container","options":{},"widgetList":[]}]}]}],"formConfig":{"jsonVersion":3}}""");
        Assert.IsTrue(WorkflowDesignerAsset.IsValid(document.RootElement));
    }

    [TestMethod]
    public void Publication_freezes_explicit_binding_identity_revision_and_actual_runtime()
    {
        using var design = JsonDocument.Parse("""{"widgetList":[{"id":"a","type":"input","options":{"name":"title","label":"原标签"}},{"id":"b","type":"input","options":{"name":"secret","defaultValue":"never-send"}}],"formConfig":{"jsonVersion":3,"modelName":"customModel"}}""");
        var schema = WorkflowDesignerAsset.Project(design.RootElement, out var diagnostic);
        Assert.IsNull(diagnostic); Assert.IsNotNull(schema);
        var artifact = WorkflowDesignerAsset.CompileBindings(design.RootElement, schema, 9);
        Assert.IsTrue(WorkflowDesignerRelease.Matches(artifact.CanonicalJson));
        using var manifest = JsonDocument.Parse(artifact.CanonicalJson);
        Assert.AreEqual(9, manifest.RootElement.GetProperty("bindingRevision").GetInt64());
        Assert.AreEqual("a", manifest.RootElement.GetProperty("bindings")[0].GetProperty("componentId").GetString());
        Assert.AreEqual("widgetList[0]", manifest.RootElement.GetProperty("bindings")[0].GetProperty("designPath").GetString());
        Assert.AreEqual("customModel", manifest.RootElement.GetProperty("bindings")[0].GetProperty("modelPath")[0].GetString());
        Assert.AreEqual(WorkflowDesignerRelease.ContentHash, manifest.RootElement.GetProperty("rendererHash").GetString());
        Assert.IsFalse(WorkflowDesignerRelease.Matches(artifact.CanonicalJson.Replace(WorkflowDesignerRelease.ContentHash, new string('0', 64), StringComparison.Ordinal)));
        var policies = new Dictionary<string, string> { ["title"] = "readOnly", ["secret"] = "hidden" };
        var view = WorkflowDesignerAsset.CreateRuntimeView(design.RootElement.GetRawText(), policies);
        Assert.IsNotNull(view);
        var authorized = WorkflowDesignerRelease.AttachAuthorizedBindings(view.Value, artifact.CanonicalJson, policies);
        Assert.IsFalse(authorized.GetRawText().Contains("secret", StringComparison.Ordinal));
        Assert.IsFalse(authorized.GetRawText().Contains("never-send", StringComparison.Ordinal));
        Assert.AreEqual(1, authorized.GetProperty("fullNetRuntime").GetProperty("bindings").GetArrayLength());
    }

    [TestMethod]
    [DataRow("{\"id\":\"a\",\"type\":\"number\",\"options\":{\"name\":\"count\"}}")]
    [DataRow("{\"id\":\"a\",\"type\":\"input\",\"options\":{\"name\":\"title\",\"validation\":\"/abc/\"}}")]
    [DataRow("{\"id\":\"a\",\"type\":\"date\",\"options\":{\"name\":\"date\",\"type\":\"datetime\"}}")]
    [DataRow("{\"id\":\"a\",\"type\":\"select\",\"options\":{\"name\":\"choice\",\"optionItems\":[{\"value\":1}]}}")]
    [DataRow("{\"id\":\"a\",\"type\":\"future-container\",\"category\":\"container\",\"options\":{}}")]
    public void Unsupported_business_semantics_do_not_discard_or_downgrade_design(string widget)
    {
        using var design = JsonDocument.Parse("{\"widgetList\":[" + widget + "],\"formConfig\":{\"jsonVersion\":3}}");
        Assert.IsTrue(WorkflowDesignerAsset.IsValid(design.RootElement));
        Assert.IsNull(WorkflowDesignerAsset.Project(design.RootElement, out var diagnostic));
        StringAssert.StartsWith(diagnostic, "widgetList[0]");
    }

    [TestMethod]
    public void Missing_vform_snapshot_is_not_treated_as_programmatic_business_schema()
    {
        Assert.IsFalse(WorkflowDesignerRelease.AllowsExecution("{\"widgetList\":[],\"formConfig\":{}}"));
        Assert.IsFalse(WorkflowDesignerRelease.AllowsExecution(null));
        Assert.IsTrue(WorkflowDesignerRelease.AllowsExecution("{\"schemaVersion\":1,\"sections\":[]}"));
    }
    [TestMethod]
    public void Integer_binding_limits_server_values_to_the_renderer_safe_range()
    {
        using var design = JsonDocument.Parse("""{"widgetList":[{"id":"a","type":"number","options":{"name":"count","fullNetFieldType":"integer","precision":0}}],"formConfig":{"jsonVersion":3}}""");
        var schema = WorkflowDesignerAsset.Project(design.RootElement, out var diagnostic);
        Assert.IsNull(diagnostic); Assert.IsNotNull(schema);
        Assert.AreEqual(9007199254740991L, schema.Sections[0].Fields[0].Constraints["maximum"].GetInt64());
        using var invalid = JsonDocument.Parse("{\"count\":9007199254740992}");
        Assert.IsFalse(WorkflowFormValueValidator.Validate(schema, invalid.RootElement));
    }

}
