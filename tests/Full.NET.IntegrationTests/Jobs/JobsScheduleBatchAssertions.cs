using System.Net;
using System.Net.Http.Json;
using Full.NET.Modules.Jobs.Contracts;

namespace Full.NET.IntegrationTests.Jobs;

/// <summary>验证任务计划批量暂停/恢复的逐项结果与乐观锁语义。</summary>
internal static class JobsScheduleBatchAssertions
{
    public static async Task VerifyAsync(
        HttpClient client,
        string token,
        HostJobScheduleResponse enabledSchedule,
        HostJobScheduleResponse pausedSchedule,
        CancellationToken cancellationToken)
    {
        await VerifyBatchPauseAsync(
            client,
            token,
            enabledSchedule,
            pausedSchedule,
            cancellationToken);
        await VerifyBatchResumeAsync(
            client,
            token,
            enabledSchedule,
            pausedSchedule,
            cancellationToken);
        await VerifyBatchPauseReportsConcurrencyConflictAsync(
            client,
            token,
            enabledSchedule,
            cancellationToken);
    }

    private static async Task VerifyBatchPauseAsync(
        HttpClient client,
        string token,
        HostJobScheduleResponse enabledSchedule,
        HostJobScheduleResponse pausedSchedule,
        CancellationToken cancellationToken)
    {
        using var request = JobsHostDefinitionAssertions.CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/jobs/host-schedules/batch-pause",
            token,
            new BatchChangeHostJobScheduleStateRequest(
            [
                new BatchChangeHostJobScheduleStateItem(
                    enabledSchedule.Id,
                    enabledSchedule.Version),
                new BatchChangeHostJobScheduleStateItem(
                    pausedSchedule.Id,
                    pausedSchedule.Version),
            ]));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content
            .ReadFromJsonAsync<BatchChangeHostJobScheduleStateResponse>(
                cancellationToken);
        Assert.IsNotNull(body);
        Assert.AreEqual(1, body.SucceededCount);
        Assert.AreEqual(2, body.Results.Count);
        Assert.IsTrue(body.Results[0].Succeeded);
        Assert.IsFalse(body.Results[0].Schedule!.IsEnabled);
        Assert.IsFalse(body.Results[1].Succeeded);
        Assert.AreEqual(
            JobsErrorCodes.ScheduleConcurrencyConflict,
            body.Results[1].ErrorCode);
    }

    private static async Task VerifyBatchResumeAsync(
        HttpClient client,
        string token,
        HostJobScheduleResponse enabledSchedule,
        HostJobScheduleResponse pausedSchedule,
        CancellationToken cancellationToken)
    {
        var paused = await ReloadScheduleAsync(
            client,
            token,
            enabledSchedule.Id,
            cancellationToken);
        Assert.IsFalse(paused.IsEnabled);

        using var request = JobsHostDefinitionAssertions.CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/jobs/host-schedules/batch-resume",
            token,
            new BatchChangeHostJobScheduleStateRequest(
            [
                new BatchChangeHostJobScheduleStateItem(
                    paused.Id,
                    paused.Version),
                new BatchChangeHostJobScheduleStateItem(
                    pausedSchedule.Id,
                    pausedSchedule.Version),
            ]));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content
            .ReadFromJsonAsync<BatchChangeHostJobScheduleStateResponse>(
                cancellationToken);
        Assert.IsNotNull(body);
        Assert.AreEqual(2, body.SucceededCount);
        Assert.IsTrue(body.Results[0].Succeeded);
        Assert.IsTrue(body.Results[0].Schedule!.IsEnabled);
        Assert.IsTrue(body.Results[1].Succeeded);
        Assert.IsTrue(body.Results[1].Schedule!.IsEnabled);
    }

    private static async Task VerifyBatchPauseReportsConcurrencyConflictAsync(
        HttpClient client,
        string token,
        HostJobScheduleResponse enabledSchedule,
        CancellationToken cancellationToken)
    {
        var current = await ReloadScheduleAsync(
            client,
            token,
            enabledSchedule.Id,
            cancellationToken);
        using var request = JobsHostDefinitionAssertions.CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/jobs/host-schedules/batch-pause",
            token,
            new BatchChangeHostJobScheduleStateRequest(
            [
                new BatchChangeHostJobScheduleStateItem(
                    current.Id,
                    current.Version - 1),
            ]));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content
            .ReadFromJsonAsync<BatchChangeHostJobScheduleStateResponse>(
                cancellationToken);
        Assert.IsNotNull(body);
        Assert.AreEqual(0, body.SucceededCount);
        Assert.AreEqual(
            JobsErrorCodes.ScheduleConcurrencyConflict,
            body.Results[0].ErrorCode);
    }

    private static async Task<HostJobScheduleResponse> ReloadScheduleAsync(
        HttpClient client,
        string token,
        Guid scheduleId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/jobs/host-schedules/{scheduleId:D}");
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var schedule = await response.Content.ReadFromJsonAsync<HostJobScheduleResponse>(
            cancellationToken);
        Assert.IsNotNull(schedule);
        return schedule;
    }
}
