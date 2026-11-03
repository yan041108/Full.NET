using System.Xml.Linq;

namespace Full.NET.Data.CodeGeneration.Integration;

/// <summary>从业务项目已经明确引用的框架路径解析静态契约依赖，不扫描工作区或引用实现模块。</summary>
internal static class ModuleIntegrationDependencies
{
    internal static string AddOrganizationContract(string source, string projectPath)
    {
        const string contract = "Full.NET.Modules.Organization.Contracts.csproj";
        var document = XDocument.Parse(source);
        var references = document.Descendants("ProjectReference").ToArray();
        if (references.Any(reference => Path.GetFileName((string?)reference.Attribute("Include")) == contract)) return source;
        var identity = references.Select(reference => (string?)reference.Attribute("Include"))
            .SingleOrDefault(path => path is not null && Path.GetFileName(path) == "Full.NET.Modules.Identity.Contracts.csproj");
        if (identity is null || identity.Contains('$'))
            throw new ArgumentException("组织归属接入要求项目具有可解析的 Identity.Contracts 项目引用，以确定同一框架的 Organization.Contracts。");
        var identityPath = Path.GetFullPath(identity, Path.GetDirectoryName(projectPath)!);
        var modules = Path.GetDirectoryName(Path.GetDirectoryName(identityPath))!;
        var dependency = Path.Combine(modules, "Full.NET.Modules.Organization.Contracts", contract);
        if (!File.Exists(dependency)) throw new ArgumentException("同一框架下缺少 Organization.Contracts 项目，不能生成不可编译的组织归属接入。");
        // 框架可以位于另一磁盘；Path.GetRelativePath 在跨盘时保留合法绝对路径。
        document.Root!.Add(new XElement("ItemGroup", new XElement("ProjectReference",
            new XAttribute("Include", Path.GetRelativePath(Path.GetDirectoryName(projectPath)!, dependency)))));
        return document.ToString() + "\n";
    }

    /// <summary>候选编译的 restore 与 build 使用同一组新增引用，不提前写入真实项目。</summary>
    internal static IEnumerable<XElement> ProjectReferences(string projectPath, string candidate)
    {
        var existing = XDocument.Load(projectPath).Descendants("ProjectReference")
            .Select(reference => (string?)reference.Attribute("Include")).ToHashSet(StringComparer.Ordinal);
        return XDocument.Parse(candidate).Descendants("ProjectReference")
            .Where(reference => !existing.Contains((string?)reference.Attribute("Include")))
            .Select(reference =>
            {
                var projected = new XElement(reference);
                projected.SetAttributeValue("Include", Path.GetFullPath((string)reference.Attribute("Include")!, Path.GetDirectoryName(projectPath)!));
                return projected;
            }).ToArray();
    }
}
