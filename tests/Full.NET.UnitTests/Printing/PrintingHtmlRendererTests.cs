using Full.NET.Modules.Printing.Domain;

namespace Full.NET.UnitTests.Printing;

[TestClass]
public sealed class PrintingHtmlRendererTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Render_rejects_placeholder_amplification_without_truncating_output(bool encoded)
    {
        var layout = string.Concat(Enumerable.Repeat("{{value}}", 1500));
        var value = new string(encoded ? '&' : 'x', 1024);
        Assert.ThrowsExactly<PrintingRenderingLimitException>(() => PrintingHtmlRenderer.Render(layout,
            new Dictionary<string, string?> { ["value"] = value }));
    }

    [TestMethod]
    public void Render_accepts_exact_output_limit_and_reuses_repeated_encoded_value()
    {
        var value = new string('x', PrintingLayoutPolicy.MaxRenderedHtmlLength / 2 - 1);
        var html = PrintingHtmlRenderer.Render("{{x}}{{x}}!!", new Dictionary<string, string?> { ["x"] = value });
        Assert.AreEqual(PrintingLayoutPolicy.MaxRenderedHtmlLength, html.Length);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Render_rejects_binding_text_or_field_count_before_encoding(bool fields)
    {
        var values = fields ? Enumerable.Range(0, 257).ToDictionary(i => "field" + i, _ => (string?)"x")
            : new Dictionary<string, string?> { ["x"] = new string('x', (int)(PrintingLayoutPolicy.MaxBindingTextBytes / 2) + 1) };
        Assert.ThrowsExactly<PrintingRenderingLimitException>(() => PrintingHtmlRenderer.Render("{{x}}", values));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Layout_budget_applies_to_sanitizing_and_rendering(bool render)
    {
        var layout = new string('x', PrintingLayoutPolicy.MaxLayoutHtmlLength + 1);
        Assert.ThrowsExactly<PrintingRenderingLimitException>(() =>
        {
            if (render) PrintingHtmlRenderer.Render(layout, new Dictionary<string, string?>());
            else PrintingHtmlRenderer.SanitizeLayoutHtml(layout);
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Cancelled_empty_render_and_sanitize_propagate_cancellation(bool render)
    {
        Assert.ThrowsExactly<OperationCanceledException>(() =>
        {
            if (render) PrintingHtmlRenderer.Render("", new Dictionary<string, string?>(), new(true));
            else PrintingHtmlRenderer.SanitizeLayoutHtml("", new(true));
        });
    }

    [TestMethod]
    [DataRow("<script>a</script><p>ok</p><script>b</script>", "<p>ok</p>")]
    [DataRow("<ScRiPt type='x'>a<em>b</em></sCrIpT><p>ok</p>", "<p>ok</p>")]
    [DataRow("<script>unfinished", "<script>unfinished")]
    public void Script_preprocessing_keeps_existing_scope_and_complete_block_semantics(string layout, string expected)
    {
        Assert.AreEqual(expected, PrintingHtmlRenderer.SanitizeLayoutHtml(layout));
    }

    [TestMethod]
    public void Render_replaces_placeholders_with_html_encoded_values()
    {
        var html = PrintingHtmlRenderer.Render(
            "<p>{{tenantName}}</p><p>{{tenantCode}}</p>",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["tenantName"] = "Acme <script>",
                ["tenantCode"] = "=evil",
            });

        Assert.Contains("Acme &lt;script&gt;", html);
        Assert.Contains("=evil", html);
    }

    [TestMethod]
    public void SanitizeLayoutHtml_removes_script_tags()
    {
        var sanitized = PrintingHtmlRenderer.SanitizeLayoutHtml(
            "<div>ok</div><script>alert(1)</script>");
        Assert.DoesNotContain("<script", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<div>ok</div>", sanitized);
    }
}
