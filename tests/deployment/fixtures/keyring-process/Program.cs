using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
using Full.NET.Hosting.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// 只检查候选已发布的注册边界；不新增业务 Host 端点，不输出密钥或异常详情。
try
{
    if (args.Length is < 1 or > 2 || args[0] is not ("protect" or "unprotect" or "inspect" or "rotate" or "backup")) return 2;
    var builder = Host.CreateApplicationBuilder();
    if (!builder.Environment.IsProduction()) return 2;
    builder.Logging.ClearProviders();
    builder.Services.AddFullNetDataProtection(builder.Configuration, builder.Environment);
    // 消费进程只读取已有密钥，拒绝场景不得自动生成新密钥污染共享介质。
    if (args[0] is "unprotect" or "inspect" or "backup") builder.Services.AddDataProtection().DisableAutomaticKeyGeneration();
    using var host = builder.Build();
    if (args[0] == "backup")
    {
        // 仅支持本轮无撤销记录的夹具介质；未知条目直接拒绝，不作为生产通用备份器。
        var root = builder.Configuration["DataProtection:KeyRingPath"] ?? throw new InvalidOperationException();
        var files = Directory.GetFiles(root, "key-*.xml");
        if (files.Length is < 1 or > 32 || Directory.GetFileSystemEntries(root).Length != files.Length) return 1;
        using var output = File.Open("/var/fullnet/recovery/keyring.zip", FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create);
        foreach (var file in files)
        {
            var info = new FileInfo(file);
            if (info.LinkTarget is not null || info.Length is < 1 or > 65536) return 1;
            using var input = File.OpenRead(file);
            using var entry = archive.CreateEntry(info.Name, CompressionLevel.NoCompression).Open();
            input.CopyTo(entry);
        }
        Console.Write(files.Length.ToString(CultureInfo.InvariantCulture));
        return 0;
    }
    if (args[0] == "rotate")
    {
        // 强制产生可区分的新密钥，调用方继续核对新密文的实际 KeyId，避免只复用旧密钥。
        var now = DateTimeOffset.UtcNow;
        var key = host.Services.GetRequiredService<IKeyManager>().CreateNewKey(now, now.AddDays(90));
        Console.Write(key.KeyId.ToString("D", CultureInfo.InvariantCulture));
        return 0;
    }
    if (args[0] == "inspect")
    {
        var root = builder.Configuration["DataProtection:KeyRingPath"] ?? throw new InvalidOperationException();
        var files = Directory.GetFiles(root, "key-*.xml");
        if (files.Length == 0) return 1;
        foreach (var file in files)
        {
            using var reader = XmlReader.Create(file, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
            var xml = XDocument.Load(reader);
            if (xml.Root?.Name.LocalName != "key" || !xml.Descendants().Any(x => x.Name.LocalName == "encryptedSecret")
                || xml.Descendants().Any(x => x.Name.LocalName == "masterKey")) return 1;
        }
        Console.Write(files.Length.ToString(CultureInfo.InvariantCulture));
        return 0;
    }
    if (args.Length != 2 || args[1] is not ("shared" or "different")) return 2;
    var input = Console.In.ReadToEnd();
    if (input.Length is < 1 or > 4096) return 2;
    var protector = host.Services.GetRequiredService<IDataProtectionProvider>()
        .CreateProtector("Full.NET.Candidate.KeyRing." + args[1]);
    Console.Write(args[0] == "protect" ? protector.Protect(input) : protector.Unprotect(input));
    return 0;
}
catch (CryptographicException)
{
    Console.Error.Write("keyring.unprotect.rejected");
    return 3;
}
catch (Exception)
{
    Console.Error.Write("keyring.probe.failed");
    return 1;
}
