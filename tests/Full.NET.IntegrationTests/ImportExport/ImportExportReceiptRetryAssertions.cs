using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.Organization.Contracts;

namespace Full.NET.IntegrationTests.ImportExport;

/// <summary>显式重试使用正式 API 与两个 Worker 宿主，不能重用已过时的业务回执。</summary>
internal static partial class ImportExportWorkerAssertions
{
    /// <summary>初次因机构停用失败，再由同编码岗位制造不同失败，验证当前回执与旧文件隔离。</summary>
    internal static async Task VerifyReceiptRetryAsync(DatabaseProvider provider, string connectionString)
    {
        var settings = Settings();
        using var api = new FullNetApiFactory(provider, connectionString, settings);
        await api.InitializeAsync();
        using var client = api.CreateClientForHost("localhost");
        var token = await ImportExportTaskAssertions.LoginAndEnterAcmeTenantAsync(client, default);
        var prepared = await PrepareReceiptTaskAsync(client, token);
        var task = prepared.Task;
        Guid? previousReceipt;
        using (var firstWorker = await BuildWorkerAsync(api, settings))
        {
            await firstWorker.StartAsync();
            try
            {
                await WaitForTaskAsync(api, task, ImportExportTaskStatusKeys.ExecutionFailed);
                previousReceipt = (await LoadTaskAsync(api, task)).ErrorReceiptFileId;
                Assert.IsNotNull(previousReceipt);
                await VerifyDownloadedReceiptAsync(client, token, task.Id, OrganizationErrorCodes.UnitNotFound);
            }
            finally { await firstWorker.StopAsync(); }
        }

        using var positionRequest = Request(HttpMethod.Post, "/api/v1/organization/positions", token);
        positionRequest.Content = JsonContent.Create(new CreateOrganizationPositionRequest(prepared.PositionCode, "Competing position", 10));
        using var positionResponse = await client.SendAsync(positionRequest);
        Assert.AreEqual(HttpStatusCode.Created, positionResponse.StatusCode, await positionResponse.Content.ReadAsStringAsync());
        using var retryRequest = Request(HttpMethod.Post, $"/api/v1/import-export/tasks/{task.Id:D}/retry", token);
        using var retryResponse = await client.SendAsync(retryRequest);
        Assert.AreEqual(HttpStatusCode.OK, retryResponse.StatusCode, await retryResponse.Content.ReadAsStringAsync());
        var queued = (await retryResponse.Content.ReadFromJsonAsync<ImportExportTaskDetailResponse>())!;
        Assert.AreEqual(ImportExportTaskStatusKeys.Queued, queued.StatusKey);
        Assert.AreEqual(0, queued.NextLineNumber);
        Assert.IsFalse(queued.HasErrorReceipt);

        using var restarted = await BuildWorkerAsync(api, settings);
        await restarted.StartAsync();
        try
        {
            var completed = await WaitForTaskAsync(api, task, ImportExportTaskStatusKeys.ExecutionFailed);
            Assert.AreEqual(1, completed.ProcessedRowCount);
            Assert.AreEqual(1, completed.ExecutionFailedRowCount);
            var record = await LoadTaskAsync(api, task);
            Assert.IsNotNull(record.ErrorReceiptFileId);
            Assert.AreNotEqual(previousReceipt, record.ErrorReceiptFileId);
            Assert.AreEqual(2, (await ListReceiptFilesAsync(api, task)).Count);
            var current = await VerifyDownloadedReceiptAsync(client, token, task.Id, OrganizationErrorCodes.PositionCodeExists);
            Assert.IsFalse(current.Contains(OrganizationErrorCodes.UnitNotFound, StringComparison.Ordinal));
        }
        finally { await restarted.StopAsync(); }
    }

    /// <summary>实际下载并检查工作簿；内部摘要不能改变用户看到的下载文件名。</summary>
    private static async Task<string> VerifyDownloadedReceiptAsync(HttpClient client, string token, Guid taskId, string expectedCode)
    {
        using var request = Request(HttpMethod.Get, $"/api/v1/import-export/tasks/{taskId:D}/error-receipt", token);
        using var response = await client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        var disposition = response.Content.Headers.ContentDisposition!;
        Assert.AreEqual("receipt-positions-errors.xlsx", (disposition.FileNameStar ?? disposition.FileName)?.Trim('"'));
        using var workbook = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
        using var zip = new ZipArchive(workbook, ZipArchiveMode.Read);
        using var sheet = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        var xml = await sheet.ReadToEndAsync();
        StringAssert.Contains(xml, expectedCode);
        return xml;
    }
}
