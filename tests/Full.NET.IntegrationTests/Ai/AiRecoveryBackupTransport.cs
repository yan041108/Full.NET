using System.Data.Common;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>仅处理本批登记的全新隔离库；不覆盖目标，也不强制断开共享连接。</summary>
internal static partial class AiRecoveryBackupTransport
{
    [GeneratedRegex("^fullnet_it_[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex DatabaseName();

    [GeneratedRegex("^/var/opt/mssql/data/a06_recovery_[0-9a-f]{32}\\.bak$", RegexOptions.CultureInvariant)]
    private static partial Regex SqlBackupPath();

    internal static async Task BackupSqlServerAsync(SqlConnection admin, string source, string path, IReadOnlySet<string> ownedDatabases, IReadOnlySet<string> ownedFiles, CancellationToken token)
    {
        ValidateDatabase(source, ownedDatabases);
        if (!SqlBackupPath().IsMatch(path) || !ownedFiles.Contains(path)) throw new InvalidOperationException("备份路径不属于本批。");
        // 仅创建准确登记的新媒体，不借 NOINIT 向已有备份追加。
        if (await admin.ExecuteScalarAsync<int>(new CommandDefinition("SELECT file_exists FROM sys.dm_os_file_exists(@Path)",
            new { Path = path }, cancellationToken: token)) != 0) throw new InvalidOperationException("备份媒体已经存在。");
        await admin.ExecuteAsync(new CommandDefinition($"BACKUP DATABASE [{source}] TO DISK=@Path WITH COPY_ONLY, NOINIT, CHECKSUM",
            new { Path = path }, commandTimeout: 180, cancellationToken: token));
    }

    internal static async Task RestoreSqlServerAsync(SqlConnection admin, string target, string path, IReadOnlySet<string> ownedDatabases, IReadOnlySet<string> ownedFiles, CancellationToken token)
    {
        ValidateDatabase(target, ownedDatabases);
        if (!SqlBackupPath().IsMatch(path) || !ownedFiles.Contains(path)) throw new InvalidOperationException("备份路径不属于本批。");
        if (await admin.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM sys.databases WHERE name=@Name",
            new { Name = target }, cancellationToken: token)) != 0) throw new InvalidOperationException("恢复目标已存在。");
        var files = (await admin.QueryAsync<SqlBackupFile>(new CommandDefinition("RESTORE FILELISTONLY FROM DISK=@Path",
            new { Path = path }, commandTimeout: 180, cancellationToken: token))).ToArray();
        if (files.Length == 0 || files.Any(x => x.Type is not ("D" or "L"))) throw new InvalidOperationException("备份文件清单不受支持。");
        var moves = string.Join(", ", files.Select(x =>
            $"MOVE N'{x.LogicalName.Replace("'", "''", StringComparison.Ordinal)}' TO N'/var/opt/mssql/data/{target}_{x.FileId}{(x.Type == "L" ? ".ldf" : ".mdf")}'"));
        await admin.ExecuteAsync(new CommandDefinition($"RESTORE DATABASE [{target}] FROM DISK=@Path WITH {moves}, CHECKSUM",
            new { Path = path }, commandTimeout: 180, cancellationToken: token));
    }

    internal static async Task ExportMySqlAsync(string rootConnection, string source, string outputPath, IReadOnlySet<string> ownedDatabases, IReadOnlySet<string> ownedFiles, CancellationToken token)
    {
        ValidateDatabase(source, ownedDatabases);
        if (!ownedFiles.Contains(outputPath)) throw new InvalidOperationException("备份路径不属于本批。");
        var root = new MySqlConnectionStringBuilder(rootConnection);
        await using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
        using var process = StartMySql("mysqldump", source, root);
        try
        {
            process.StandardInput.Close();
            var errors = process.StandardError.ReadToEndAsync(token);
            await process.StandardOutput.BaseStream.CopyToAsync(output, token);
            await process.WaitForExitAsync(token);
            if (process.ExitCode != 0) throw new InvalidOperationException("MySQL 备份进程失败，退出码 " + process.ExitCode);
            _ = await errors;
        }
        finally { await StopOwnedProcessAsync(process); }
    }

    internal static async Task ImportMySqlAsync(string rootConnection, string target, string inputPath, IReadOnlySet<string> ownedDatabases, IReadOnlySet<string> ownedFiles, CancellationToken token)
    {
        ValidateDatabase(target, ownedDatabases);
        if (!ownedFiles.Contains(inputPath)) throw new InvalidOperationException("备份路径不属于本批。");
        var root = new MySqlConnectionStringBuilder(rootConnection) { Database = "information_schema", Pooling = false };
        await using (var admin = new MySqlConnection(root.ConnectionString))
        {
            await admin.OpenAsync(token);
            var exists = await admin.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM SCHEMATA WHERE SCHEMA_NAME=@Name", new { Name = target }, cancellationToken: token));
            var tables = await admin.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM TABLES WHERE TABLE_SCHEMA=@Name", new { Name = target }, cancellationToken: token));
            if (exists != 1 || tables != 0) throw new InvalidOperationException("恢复目标必须是本批已登记的全新空库。");
        }
        await using var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        using var process = StartMySql("mysql", target, root);
        try
        {
            var errors = process.StandardError.ReadToEndAsync(token);
            var stdout = process.StandardOutput.ReadToEndAsync(token);
            await input.CopyToAsync(process.StandardInput.BaseStream, token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(token);
            if (process.ExitCode != 0) throw new InvalidOperationException("MySQL 恢复进程失败，退出码 " + process.ExitCode);
            _ = await errors;
            _ = await stdout;
        }
        finally { await StopOwnedProcessAsync(process); }
    }

    internal static Process StartMySql(string operation, string database, MySqlConnectionStringBuilder root)
    {
        const string directory = "/out/temp/a06-mysql-cli-v1";
        var binary = Path.Combine(directory, operation);
        var expected = operation switch
        {
            "mysqldump" => "179CD6E5073FA18984949D89F51603B8062F6716BBF4BE95BD8EBC889022D4D9",
            "mysql" => "CCE01A9D401A29E354268014F5203E494FA5B1EE8E9F703F6F5158CEA0EBE607",
            _ => throw new InvalidOperationException("不支持的备份工具。")
        };
        using (var file = File.OpenRead(binary))
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(file)), expected, StringComparison.Ordinal))
                throw new InvalidOperationException("备份工具身份不匹配。");
        using (var file = File.OpenRead(Path.Combine(directory, "libncurses.so.6")))
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(file)), "A5717B7258321918C0F6115FA8DA3B2BE468FFB5C80C31C8861256BD6EE746B9", StringComparison.Ordinal))
                throw new InvalidOperationException("备份工具依赖身份不匹配。");
        if (string.IsNullOrEmpty(root.Password) || root.UserID != "root" || root.SslMode != MySqlSslMode.Preferred)
            throw new InvalidOperationException("测试管理连接的身份或 TLS 模式与已核对配置不符。");
        // 直接运行独立的官方客户端，取消时持有真实子进程句柄；不通过 docker exec 遗留容器内孤儿。
        var info = new ProcessStartInfo(binary) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        info.Environment["MYSQL_PWD"] = root.Password;
        info.Environment["LD_LIBRARY_PATH"] = directory;
        foreach (var arg in new[] { "--protocol=TCP", "--host", root.Server, "--port", root.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), "--user=root", "--ssl-mode=PREFERRED" }) info.ArgumentList.Add(arg);
        if (operation == "mysqldump")
            foreach (var arg in new[] { "--single-transaction", "--routines", "--triggers", "--hex-blob", "--set-gtid-purged=OFF", "--skip-comments", "--skip-dump-date" }) info.ArgumentList.Add(arg);
        info.ArgumentList.Add("--");
        info.ArgumentList.Add(database);
        return Process.Start(info) ?? throw new InvalidOperationException("无法启动 MySQL 备份工具。");
    }

    private static void ValidateDatabase(string name, IReadOnlySet<string> ownedDatabases)
    {
        if (!DatabaseName().IsMatch(name) || !ownedDatabases.Contains(name)) throw new InvalidOperationException("数据库不属于本批精确登记范围。");
    }

    internal static async Task StopOwnedProcessAsync(Process process)
    {
        if (process.HasExited) return;
        try { process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { return; }
        await process.WaitForExitAsync(CancellationToken.None);
    }

    private sealed class SqlBackupFile
    {
        public string LogicalName { get; set; } = "";
        public string Type { get; set; } = "";
        public long FileId { get; set; }
    }
}
