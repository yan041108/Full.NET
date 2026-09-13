using System.Xml.Linq;

namespace Full.NET.ArchitectureTests;

/// <summary>将新 AI 目录纳入依赖门禁，防止通过新目录绕开业务模块边界。</summary>
[TestClass]
public sealed class AiDependencyBoundaryTests
{
    private static readonly HashSet<string> McpServiceTokenDecryptionAllowlist = new(StringComparer.Ordinal)
    {
        "src/Modules/Full.NET.Modules.Ai/Mcp/AiMcpRemoteTokenProtector.cs",
        "src/Modules/Full.NET.Modules.Ai/Mcp/AiMcpRemoteToolCatalog.cs",
        "src/Modules/Full.NET.Modules.Ai/Features/ManageMcpRemoteConnections/AiMcpRemoteConnectionManagementService.cs",
    };
    [TestMethod]
    public void Agent_framework_sdk_stays_inside_the_replaceable_adapter()
    {
        var package = XDocument.Load(Path.Combine(Root(), "Directory.Packages.props"))
            .Descendants("PackageVersion").Single(item => item.Attribute("Include")?.Value == "Microsoft.Agents.AI");
        Assert.AreEqual(Full.NET.Agents.Runtime.AgentFrameworkRuntime.FrameworkVersion, package.Attribute("Version")!.Value,
            "持久化会话版本必须随实际 SDK 一起演进。");
        var directory = Path.Combine(Root(), "src", "AI", "Full.NET.Agents");
        foreach (var source in Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin" or "Framework")))
        {
            Assert.DoesNotContain("Microsoft.Agents.AI", File.ReadAllText(source), StringComparison.Ordinal, source);
        }
    }

    [TestMethod]
    public void Chat_execution_does_not_depend_on_http_transport()
    {
        var source = File.ReadAllText(Path.Combine(Root(), "src", "Modules", "Full.NET.Modules.Ai",
            "Features", "ManageChatSessions", "AiChatStreamService.cs"));
        Assert.DoesNotContain("HttpContext", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.AspNetCore.Http", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AiChatSseWriter", source, StringComparison.Ordinal);
    }

    [TestMethod]
    public void Ai_business_module_has_no_credential_decryption_or_provider_http_paths()
    {
        var sources = Directory.GetFiles(Path.Combine(Root(), "src", "Modules", "Full.NET.Modules.Ai"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin"));
        foreach (var source in sources)
        {
            var relativePath = Path.GetRelativePath(Root(), source).Replace('\\', '/');
            var text = File.ReadAllText(source);
            if (!McpServiceTokenDecryptionAllowlist.Contains(relativePath))
            {
                Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(text, @"\.Unprotect\s*\("), source);
            }
            Assert.DoesNotContain("/api/tags", text, StringComparison.Ordinal, source);
            Assert.DoesNotContain("/chat/completions", text, StringComparison.Ordinal, source);
        }
    }

    [TestMethod]
    public void Ai_projects_do_not_reference_business_modules()
    {
        var projects = Directory.GetFiles(Path.Combine(Root(), "src", "AI"), "*.csproj", SearchOption.AllDirectories);
        Assert.IsTrue(projects.Length >= 4);
        foreach (var project in projects)
        {
            var references = XDocument.Load(project).Descendants("ProjectReference")
                .Select(item => item.Attribute("Include")!.Value);
            Assert.IsFalse(references.Any(value =>
                value.Contains("Full.NET.Modules.", StringComparison.Ordinal)
                && !value.Contains(".Contracts", StringComparison.Ordinal)), project);
        }
    }

    [TestMethod]
    public void Ai_business_module_does_not_reference_provider_assemblies()
    {
        var dependencies = typeof(Full.NET.Modules.Ai.AiModule).Assembly.GetReferencedAssemblies();
        Assert.IsFalse(dependencies.Any(item => item.Name!.StartsWith("Full.NET.AI.Providers.", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Ai_abstractions_do_not_depend_on_web_or_provider_implementations()
    {
        var project = XDocument.Load(Path.Combine(Root(), "src", "AI", "Full.NET.AI.Abstractions", "Full.NET.AI.Abstractions.csproj"));
        Assert.IsFalse(project.Descendants("FrameworkReference").Any());
        Assert.IsFalse(project.Descendants("ProjectReference").Any());
        var packages = project.Descendants("PackageReference").Select(item => item.Attribute("Include")!.Value).ToArray();
        CollectionAssert.AreEqual(new[] { "Microsoft.Extensions.AI.Abstractions" }, packages);
    }

    private static string Root()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Full.NET.slnx"))) current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException("未找到仓库根目录。");
    }
}
