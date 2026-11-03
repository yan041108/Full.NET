using System.Diagnostics;
using System.Text.Json;
using Full.NET.IntegrationTests.Api;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Workflow;

public sealed partial class WorkflowCollaborationRequestApiTests
{
    /// <summary>源成员在真实Vue中添加类型20并保存/发布；子进程只获得测试登录凭据与本例非敏感关联键。</summary>
    private sealed class PublicNodeAuthoringBrowser(string origin, string username)
    {
        internal async Task<Guid> PublishAsync(FullNetApiFactory factory, HttpClient source, Asset asset, Guid grant)
        {
            var temporary = Directory.CreateTempSubdirectory("fullnet-workflow-public-ui-").FullName;
            var repository = FullNetApiFactory.FindRepositoryRoot(); var passed = false; Process? process = null;
            try
            {
                var start = new ProcessStartInfo("node") { WorkingDirectory = Path.Combine(repository, "tests", "e2e", "admin-real-stack"),
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                foreach (var argument in new[] { Path.Combine(start.WorkingDirectory, "node_modules", "@playwright", "test", "cli.js"), "test",
                    "workflow-collaboration-public-node.spec.mjs", "--project", "vue-admin", "--workers", "1", "--output", Path.Combine(temporary, "browser") }) start.ArgumentList.Add(argument);
                foreach (var key in new[] { "BOOTSTRAP", "VIEWER_PROVISION", "OIDC_FIXTURES" }) start.Environment["FULLNET_E2E_SKIP_" + key] = "1";
                start.Environment["FULLNET_E2E_API_URL"] = factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
                start.Environment["FULLNET_E2E_ADMIN_ORIGIN"] = origin; start.Environment["FULLNET_E2E_PASSWORD"] = FullNetApiFactory.TestPassword;
                start.Environment["FULLNET_E2E_PUBLIC_USERNAME"] = username; start.Environment["FULLNET_E2E_PUBLIC_DEFINITION"] = asset.Definition.ToString("D");
                start.Environment["FULLNET_E2E_PUBLIC_KEY"] = asset.Key; start.Environment["FULLNET_E2E_PUBLIC_FORM"] = asset.Form.ToString("D");
                start.Environment["FULLNET_E2E_PUBLIC_GRANT"] = grant.ToString("D"); start.Environment["FULLNET_E2E_PUBLIC_DIRECTORY"] = temporary;
                start.Environment["FULLNET_E2E_L3_JSON_REPORT"] = Path.Combine(temporary, "browser.json");
                process = Process.Start(start)!; var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(6)); await process.WaitForExitAsync(timeout.Token);
                Assert.AreEqual(0, process.ExitCode, await output + Environment.NewLine + await errors + Environment.NewLine + temporary);
                using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(temporary, "browser.json")));
                var stats = report.RootElement.GetProperty("stats"); Assert.AreEqual(1, stats.GetProperty("expected").GetInt32());
                Assert.AreEqual(0, stats.GetProperty("unexpected").GetInt32()); Assert.AreEqual(0, stats.GetProperty("skipped").GetInt32());
                using var result = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(temporary, "result.json")));
                var version = result.RootElement.GetProperty("versionId").GetGuid(); Assert.AreNotEqual(Guid.Empty, version);
                // 浏览器登录会关闭准备会话；通过正常登录重新取得来源本人的会话，不能豁免单会话约束。
                source.DefaultRequestHeaders.Authorization = null;
                source.DefaultRequestHeaders.Remove("Origin"); source.DefaultRequestHeaders.Add("Origin", "http://localhost");
                source.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(source, username));
                passed = true; return version;
            }
            finally
            {
                if (process is { HasExited: false }) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
                process?.Dispose(); if (passed) Directory.Delete(temporary, recursive: true); else Console.WriteLine("公共协作浏览器证据：" + temporary);
            }
        }
    }
}
