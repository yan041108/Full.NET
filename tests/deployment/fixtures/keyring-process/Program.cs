using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
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
    if (args.Length is < 1 or > 2 || args[0] is not ("protect" or "unprotect" or "inspect" or "rotate" or "backup"
        or "consumer-protect" or "consumer-unprotect" or "consumer-inventory")) return 2;
    var builder = Host.CreateApplicationBuilder();
    if (!builder.Environment.IsProduction()) return 2;
    builder.Logging.ClearProviders();
    builder.Services.AddFullNetDataProtection(builder.Configuration, builder.Environment);
    // 消费进程只读取已有密钥，拒绝场景不得自动生成新密钥污染共享介质。
    if (args[0] is "unprotect" or "inspect" or "backup" or "consumer-unprotect" or "consumer-inventory")
        builder.Services.AddDataProtection().DisableAutomaticKeyGeneration();
    using var host = builder.Build();
    if (args[0].StartsWith("consumer-", StringComparison.Ordinal))
    {
        if (args[0] == "consumer-inventory")
        {
            foreach (var module in new[] { "Identity", "Notifications", "Payments" })
            {
                var name = "Full.NET.Modules." + module + ".dll";
                Console.WriteLine(name + "=" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes("/app/" + name))));
            }
            return 0;
        }
        if (args.Length != 2) return 2;
        var value = Console.In.ReadToEnd();
        if (value.Length is < 1 or > 4096) return 2;
        // 仅 JIT 验收使用固定类型清单；依赖严格从候选 Host 的 deps 闭包解析，不扩大产品可见性。
        var entry = File.Exists("/app/Full.NET.Host.Api.dll") ? "/app/Full.NET.Host.Api.dll" : "/app/Full.NET.Host.Worker.dll";
        var resolver = new AssemblyDependencyResolver(entry);
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            var path = resolver.ResolveAssemblyToPath(name);
            return path is not null && path.StartsWith("/app/", StringComparison.Ordinal)
                ? context.LoadFromAssemblyPath(path) : null;
        };
        Console.Write(InvokeConsumer(host.Services.GetRequiredService<IDataProtectionProvider>(), args[1], value,
            args[0] == "consumer-protect"));
        return 0;
    }
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
            using var keyInput = File.OpenRead(file);
            using var entry = archive.CreateEntry(info.Name, CompressionLevel.NoCompression).Open();
            keyInput.CopyTo(entry);
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
catch (TargetInvocationException exception) when (exception.InnerException is CryptographicException)
{
    Console.Error.Write("keyring.unprotect.rejected");
    return 3;
}
catch (Exception exception)
{
    Console.Error.Write("keyring.probe.failed." + exception.GetType().Name);
    return 1;
}

// 只构造候选中真实的保护器组件，不启动渠道客户端、任务处理器或业务 Host。
static string InvokeConsumer(IDataProtectionProvider provider, string alias, string value, bool protect)
{
    var (module, typeName, protectMethod, unprotectMethod) = alias switch
    {
        "notification" => ("Notifications", "Domain.NotificationRecipientEndpointProtector", "Protect", "Unprotect"),
        "ldap" => ("Identity", "Security.LdapBindPasswordProtector", "Protect", "Unprotect"),
        "oauth" => ("Identity", "Security.OAuthClientSecretProtector", "Protect", "Unprotect"),
        "totp" => ("Identity", "Security.TotpSecretProtector", "Protect", "Unprotect"),
        "payment-api" => ("Payments", "Security.PaymentSecretProtector", "ProtectApiV3Key", "UnprotectApiV3Key"),
        "payment-private" => ("Payments", "Security.PaymentSecretProtector", "ProtectPrivateKey", "UnprotectPrivateKey"),
        _ => throw new ArgumentException("consumer.alias.invalid")
    };
    var name = "Full.NET.Modules." + module;
    var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath("/app/" + name + ".dll");
    var type = assembly.GetType(name + "." + typeName, throwOnError: true)!;
    var instance = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        binder: null, args: [provider], culture: CultureInfo.InvariantCulture) ?? throw new InvalidOperationException();
    var method = type.GetMethod(protect ? protectMethod : unprotectMethod, [typeof(string)]) ?? throw new InvalidOperationException();
    return method.Invoke(instance, [value]) as string ?? throw new InvalidOperationException();
}
