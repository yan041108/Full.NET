using Full.NET.Modules.Printing.Domain;

namespace Full.NET.UnitTests.Printing;

[TestClass]
public sealed class PrintingHtmlRendererTests
{
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
