// 仅用于镜像交付链验收，不启动业务宿主、监听端口或访问外部服务。
if (args.Length != 1 || args[0] is not ("legacy" or "current"))
{
    return 1;
}

// 直接检查容器进程身份，不能由 docker run 的 --user 参数掩盖镜像配置错误。
var uid = File.ReadLines("/proc/self/status").Single(line => line.StartsWith("Uid:", StringComparison.Ordinal))
    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Skip(1).ToArray();
if (uid.Length != 4 || uid.Any(value => value != "1654")
    || Directory.GetCurrentDirectory() != "/app"
    || Environment.GetEnvironmentVariable("DOTNET_EnableDiagnostics") != "0"
    || Environment.GetEnvironmentVariable("ASPNETCORE_URLS") != "http://+:8080")
{
    return 2;
}

// 离线更新必须读到新内容，且不能通过新旧应用层叠加保留旧文件。
if (File.ReadAllText("/app/candidate.txt") != args[0]
    || File.Exists("/app/old-only.txt") != (args[0] == "legacy")
    || File.ReadAllText("/app/.runtime-dotfile") != "preserved")
{
    return 3;
}

var assembly = typeof(Program).Assembly.GetName().Name;
if (assembly is not ("Full.NET.Host.Api" or "Full.NET.Host.Worker" or "Full.NET.Host.Migrator"))
{
    return 4;
}

Console.WriteLine($"fullnet_oci_runtime:{assembly}:{args[0]}:1654:{Environment.Version}");
return 0;
