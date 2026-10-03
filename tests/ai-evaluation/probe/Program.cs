using System.Runtime.CompilerServices;
using System.Text.Json;
using Full.NET.AiRetrieval.Probe;

// 所有输入仅来自实验控制器；凭据不进入报告或失败输出。
try
{
    var mode = args[0];
    var corpus = ProbeCorpus.Read(args[1]);
    var observations = new List<ProbeObservation>();
    string version;
    double recall;
    if (mode == "parser")
    {
        ProbeParser.Run(observations);
        version = "PdfPig 0.1.16";
        recall = 0;
    }
    else if (mode == "qdrant")
    {
        (version, recall) = await ProbeQdrant.RunAsync(corpus.Chunks, corpus.Cases, observations);
    }
    else if (mode is "sqlserver" or "mysql")
    {
        (version, recall) = await ProbeSql.RunAsync(mode, corpus.Chunks, corpus.Cases, observations);
    }
    else throw new ArgumentException("未知实验模式。");
    var report = new ProbeReport(mode, RuntimeFeature.IsDynamicCodeSupported ? "JIT" : "NativeAOT",
        version, ProbeCorpus.Digest, ProbeCorpus.Model, "not_measured", recall,
        corpus.Chunks.Length, ProbeCorpus.Dimension, observations.ToArray());
    File.WriteAllText(args[2], JsonSerializer.Serialize(report, ProbeJsonContext.Default.ProbeReport) + "\n");
    if (observations.Any(x => !x.Passed)) return 1;
    Console.WriteLine($"PASS {mode}: {observations.Count} observations; runtime={report.Runtime}");
    return 0;
}
catch (Exception exception)
{
    if (exception is Microsoft.Data.SqlClient.SqlException sql)
        Console.Error.WriteLine($"SQL error number={sql.Number}, state={sql.State}, class={sql.Class}");
    if (exception is MySqlConnector.MySqlException mysql)
        Console.Error.WriteLine($"MySQL error code={mysql.ErrorCode}");
    Console.Error.WriteLine($"FAIL probe: {exception.GetType().Name}\n{exception.StackTrace}");
    return 1;
}
