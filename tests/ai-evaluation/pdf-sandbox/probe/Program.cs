using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

// 独立测试二进制只从 stdin 选择对抗模式；生产解析器没有这些入口。
var mode = Console.ReadLine();
if (RuntimeFeature.IsDynamicCodeSupported) return 3;
if (mode == "isolation")
{
    var blocked = false;
    try
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Connect(new IPEndPoint(IPAddress.Parse("198.51.100.1"), 443));
    }
    catch (SocketException exception) { blocked = exception.SocketErrorCode is SocketError.NetworkUnreachable or SocketError.HostUnreachable; }
    var readOnly = false;
    try { File.WriteAllText("/app/forbidden", "x"); }
    catch (UnauthorizedAccessException) { readOnly = true; }
    catch (IOException) { readOnly = true; }
    var status = File.ReadAllLines("/proc/self/status");
    using var writer = new Utf8JsonWriter(Console.OpenStandardOutput());
    writer.WriteStartObject();
    writer.WriteBoolean("native", !RuntimeFeature.IsDynamicCodeSupported);
    writer.WriteBoolean("networkBlocked", blocked);
    writer.WriteBoolean("loopbackOnly", NetworkInterface.GetAllNetworkInterfaces().All(item => item.NetworkInterfaceType == NetworkInterfaceType.Loopback));
    writer.WriteBoolean("readOnly", readOnly);
    writer.WriteBoolean("nonRoot", status.Single(line => line.StartsWith("Uid:", StringComparison.Ordinal)).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Skip(1).All(value => value == "65532"));
    writer.WriteBoolean("noPrivileges", status.Contains("NoNewPrivs:\t1", StringComparer.Ordinal));
    writer.WriteBoolean("zeroCapabilities", status.Contains("CapEff:\t0000000000000000", StringComparer.Ordinal));
    writer.WriteBoolean("noCredentials", Environment.GetEnvironmentVariables().Keys.Cast<string>().All(key => key is "PATH" or "APP_UID" or "ASPNETCORE_HTTP_PORTS" or "DOTNET_RUNNING_IN_CONTAINER" or "HOSTNAME" or "HOME"));
    writer.WriteString("memoryMax", File.ReadAllText("/sys/fs/cgroup/memory.max").Trim());
    writer.WriteString("swapMax", File.ReadAllText("/sys/fs/cgroup/memory.swap.max").Trim());
    writer.WriteString("cpuMax", File.ReadAllText("/sys/fs/cgroup/cpu.max").Trim());
    writer.WriteString("pidsMax", File.ReadAllText("/sys/fs/cgroup/pids.max").Trim());
    writer.WriteEndObject();
    return 0;
}
if (mode is "hang" or "memory")
{
    Console.WriteLine("started");
    Console.Out.Flush();
    if (mode == "hang") { while (true) Thread.SpinWait(100000); }
    // 用真实 native 分配绕过托管堆软限制，确认 cgroup 的实际 OOM 强杀。
    ExhaustMemory();
}
if (mode is "stdout" or "stderr")
{
    var stream = mode == "stdout" ? Console.OpenStandardOutput() : Console.OpenStandardError();
    var block = Encoding.ASCII.GetBytes(new string('x', 65536));
    while (true) stream.Write(block);
}
return 2;

static unsafe void ExhaustMemory()
{
    var pointers = new List<nint>();
    while (true)
    {
        var pointer = NativeMemory.Alloc(32 * 1024 * 1024);
        new Span<byte>(pointer, 32 * 1024 * 1024).Fill(0x42);
        pointers.Add((nint)pointer);
    }
}
