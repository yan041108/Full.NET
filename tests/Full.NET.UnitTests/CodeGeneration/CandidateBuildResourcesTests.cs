using System.Diagnostics;
using Full.NET.Data.CodeGeneration.Integration;

namespace Full.NET.UnitTests.CodeGeneration;

/// <summary>资源预算必须同时约束 MSBuild 与托管编译器，不改写候选输入和隔离输出参数。</summary>
[TestClass]
public sealed class CandidateBuildResourcesTests
{
    [TestMethod]
    [DataRow(1, "1")]
    [DataRow(64, "2")]
    public void Candidate_build_keeps_projection_arguments_and_bounds_all_child_work(int processors, string budget)
    {
        var start = new ProcessStartInfo("dotnet");
        string[] original = ["build", "path with spaces/Catalog.csproj", "--artifacts-path", "temporary/build"];
        foreach (var argument in original) start.ArgumentList.Add(argument);
        CandidateBuildResources.Configure(start, processors);
        CollectionAssert.AreEqual(original, start.ArgumentList.Take(original.Length).ToArray());
        Assert.AreEqual("-maxcpucount:" + budget, start.ArgumentList[original.Length]);
        Assert.Contains("-p:UseSharedCompilation=false", start.ArgumentList);
        Assert.AreEqual(budget, start.Environment["DOTNET_PROCESSOR_COUNT"]);
        Assert.AreEqual("1", start.Environment["MSBUILDDISABLENODEREUSE"]);
    }
}
