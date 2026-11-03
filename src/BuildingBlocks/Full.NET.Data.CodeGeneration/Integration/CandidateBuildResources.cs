using System.Diagnostics;
using System.Globalization;

namespace Full.NET.Data.CodeGeneration.Integration;

/// <summary>候选编译显式约束 MSBuild 节点和编译器预算，避免子进程脱离调用方的资源限制。</summary>
internal static class CandidateBuildResources
{
    internal static void Configure(ProcessStartInfo startInfo, int processorCount)
    {
        var budget = Math.Clamp(processorCount, 1, 2).ToString(CultureInfo.InvariantCulture);
        startInfo.ArgumentList.Add("-maxcpucount:" + budget);
        startInfo.ArgumentList.Add("-p:UseSharedCompilation=false");
        startInfo.Environment["DOTNET_PROCESSOR_COUNT"] = budget;
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
    }
}
