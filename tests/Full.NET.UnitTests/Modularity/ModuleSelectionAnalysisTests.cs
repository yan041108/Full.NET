using Full.NET.Composition;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Document;
using Full.NET.Modules.Identity;
using Microsoft.Extensions.Configuration;

namespace Full.NET.UnitTests.Modularity;

/// <summary>
/// 校验模块启用集分析器对 DAG 与名称规则的报告行为。
/// </summary>
[TestClass]
public sealed class ModuleSelectionAnalysisTests
{
    [TestMethod]
    public void AnalyzeConfiguration_reports_valid_full_preset()
    {
        var analysis = FullNetModuleSelection.AnalyzeConfiguration(
            CreateConfiguration(),
            [new IdentityModule(), new DocumentModule()]);

        Assert.IsTrue(analysis.IsValid);
        Assert.AreEqual(ModuleSelectionSourceKinds.Preset, analysis.SourceKind);
        Assert.AreEqual(FullNetModuleSelectionOptions.Presets.Full, analysis.Preset);
        Assert.IsEmpty(analysis.Issues);
    }

    [TestMethod]
    public void AnalyzeOptions_reports_missing_dependency_for_document_without_files()
    {
        var analysis = FullNetModuleSelection.AnalyzeOptions(
            new FullNetModuleSelectionOptions
            {
                Enabled = ["Identity", "Document"],
            },
            [new IdentityModule(), new DocumentModule()]);

        Assert.IsFalse(analysis.IsValid);
        Assert.IsTrue(analysis.Issues.Any(issue =>
            issue.Code == ModuleSelectionIssueCodes.MissingDependency
            && issue.ModuleKey == "Document"
            && issue.RelatedModuleKey == "Files"));
        var documentState = analysis.ModuleStates.Single(state => state.ModuleKey == "Document");
        CollectionAssert.AreEquivalent(
            new[] { "Files" },
            documentState.MissingDependencies.ToArray());
    }

    [TestMethod]
    public void AnalyzeOptions_reports_unknown_preset_without_enabling_modules()
    {
        var analysis = FullNetModuleSelection.AnalyzeOptions(
            new FullNetModuleSelectionOptions { Preset = "Typo" },
            [new IdentityModule()]);

        Assert.IsFalse(analysis.IsValid);
        Assert.IsEmpty(analysis.EnabledModuleKeys);
        Assert.HasCount(1, analysis.Issues);
        Assert.AreEqual(ModuleSelectionIssueCodes.UnknownPreset, analysis.Issues[0].Code);
    }

    [TestMethod]
    public void AnalyzeOptions_reports_explicit_empty_list()
    {
        var analysis = FullNetModuleSelection.AnalyzeOptions(
            new FullNetModuleSelectionOptions { Enabled = [] },
            [new IdentityModule()]);

        Assert.IsFalse(analysis.IsValid);
        Assert.AreEqual(ModuleSelectionSourceKinds.Explicit, analysis.SourceKind);
        Assert.IsTrue(analysis.Issues.Any(issue => issue.Code == ModuleSelectionIssueCodes.EmptyEnabled));
    }

    private static IConfiguration CreateConfiguration(
        IReadOnlyDictionary<string, string?>? values = null)
    {
        var builder = new ConfigurationBuilder();
        if (values is not null)
        {
            builder.AddInMemoryCollection(values);
        }

        return builder.Build();
    }
}
