using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

/// <summary>验证业务标题模板解析与边界规则。</summary>
[TestClass]
public sealed class WorkflowBusinessTitleRulesTests
{
    [TestMethod]
    public void Resolve_simple_placeholder_returns_title()
    {
        using var document = JsonDocument.Parse("""{"summary":"采购申请"}""");
        var title = WorkflowBusinessTitleRules.Resolve("审批：{summary}", document.RootElement);
        Assert.AreEqual("审批：采购申请", title);
    }

    [TestMethod]
    public void Resolve_nested_field_reads_dotted_path()
    {
        using var document = JsonDocument.Parse("""{"request":{"code":"PO-001"}}""");
        var title = WorkflowBusinessTitleRules.Resolve("{request.code}", document.RootElement);
        Assert.AreEqual("PO-001", title);
    }

    [TestMethod]
    public void Resolve_missing_placeholder_returns_null()
    {
        using var document = JsonDocument.Parse("{}");
        Assert.IsNull(WorkflowBusinessTitleRules.Resolve("{summary}", document.RootElement));
    }

    [TestMethod]
    public void Resolve_blank_template_returns_null()
    {
        using var document = JsonDocument.Parse("""{"summary":"x"}""");
        Assert.IsNull(WorkflowBusinessTitleRules.Resolve("   ", document.RootElement));
    }

    [TestMethod]
    public void NormalizeTitle_blank_input_returns_null()
    {
        Assert.IsNull(WorkflowBusinessTitleRules.NormalizeTitle("   "));
    }

    [TestMethod]
    public void IsValidTemplate_over_256_chars_fails()
    {
        Assert.IsFalse(WorkflowBusinessTitleRules.IsValidTemplate(new string('a', 257)));
    }

    [TestMethod]
    public void IsValidTitle_explicit_override_passes()
    {
        Assert.IsTrue(WorkflowBusinessTitleRules.IsValidTitle("数据审批 · PO-001"));
    }
}
