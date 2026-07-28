using System.Globalization;
using System.Xml.Linq;

namespace Full.NET.Benchmarks.Auditing;

public sealed record AuditingSqlServerPlanMetrics(
    long CompileTimeMilliseconds,
    long CompileCpuMilliseconds,
    long CompileMemoryKilobytes,
    long ElapsedTimeMilliseconds,
    long CpuTimeMilliseconds,
    long ActualLogicalReads,
    long ActualRowsRead,
    bool RetrievedFromCache)
{
    public static AuditingSqlServerPlanMetrics Parse(string showPlanXml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(showPlanXml);
        var document = XDocument.Parse(showPlanXml);
        var queryPlans = document
            .Descendants()
            .Where(element => element.Name.LocalName == "QueryPlan")
            .ToArray();
        var queryTimeStatistics = document
            .Descendants()
            .Where(element => element.Name.LocalName == "QueryTimeStats")
            .ToArray();
        var runtimeCounters = document
            .Descendants()
            .Where(element => element.Name.LocalName == "RunTimeCountersPerThread")
            .ToArray();
        var statementNodes = document
            .Descendants()
            .Where(element => element.Name.LocalName == "StmtSimple")
            .ToArray();

        return new AuditingSqlServerPlanMetrics(
            SumAttributes(queryPlans, "CompileTime"),
            SumAttributes(queryPlans, "CompileCPU"),
            SumAttributes(queryPlans, "CompileMemory"),
            SumAttributes(queryTimeStatistics, "ElapsedTime"),
            SumAttributes(queryTimeStatistics, "CpuTime"),
            SumAttributes(runtimeCounters, "ActualLogicalReads"),
            SumAttributes(runtimeCounters, "ActualRowsRead"),
            statementNodes.Any(
                element => string.Equals(
                    element.Attribute("RetrievedFromCache")?.Value,
                    "true",
                    StringComparison.OrdinalIgnoreCase)));
    }

    private static long SumAttributes(
        IEnumerable<XElement> elements,
        string attributeName) =>
        elements.Sum(
            element => long.TryParse(
                element.Attribute(attributeName)?.Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var value)
                ? value
                : 0L);
}
