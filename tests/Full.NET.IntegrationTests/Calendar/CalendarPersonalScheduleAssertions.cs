using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Calendar.Contracts;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.IntegrationTests.Calendar;

/// <summary>个人日程纵向切片验收夹具。</summary>
internal static class CalendarPersonalScheduleAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        await OpenApiCalendarPersonalSchedulesContractAssertions.VerifyAsync(
            client,
            cancellationToken);

        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        await GetCurrentUserAsync(client, adminToken, cancellationToken);

        var created = await CreateScheduleAsync(
            client,
            adminToken,
            "集成测试日程",
            DateTimeOffset.UtcNow.AddHours(1),
            DateTimeOffset.UtcNow.AddHours(2),
            cancellationToken);
        Assert.AreEqual(PersonalScheduleStatuses.Pending, created.Status);
        Assert.AreEqual(1, created.Version);

        var fetched = await GetScheduleAsync(
            client,
            adminToken,
            created.Id,
            cancellationToken);
        Assert.AreEqual(created.Id, fetched.Id);
        Assert.AreEqual(created.Content, fetched.Content);

        var updated = await UpdateScheduleAsync(
            client,
            adminToken,
            created.Id,
            new UpdatePersonalScheduleRequest(
                "更新后的日程",
                created.StartAtUtc,
                created.EndAtUtc.AddMinutes(30),
                created.Version),
            cancellationToken);
        Assert.AreEqual("更新后的日程", updated.Content);
        Assert.AreEqual(2, updated.Version);

        var completed = await SetStatusAsync(
            client,
            adminToken,
            created.Id,
            new SetPersonalScheduleStatusRequest(
                PersonalScheduleStatuses.Completed,
                updated.Version),
            cancellationToken);
        Assert.AreEqual(PersonalScheduleStatuses.Completed, completed.Status);
        Assert.IsNotNull(completed.CompletedAtUtc);

        await VerifyTimeRangeFilterAsync(
            client,
            adminToken,
            created.StartAtUtc,
            created.EndAtUtc,
            created.Id,
            cancellationToken);

        await VerifyCrossUserNotFoundAsync(
            factory,
            client,
            adminToken,
            created.Id,
            cancellationToken);

        await VerifyConcurrencyConflictAsync(
            client,
            adminToken,
            created.Id,
            completed.Version - 1,
            cancellationToken);

        using var deleteRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/calendar/my-personal-schedules/{created.Id:D}/delete",
            adminToken,
            new ChangePersonalScheduleRequest(completed.Version));
        using var deleteResponse = await client.SendAsync(deleteRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using var missingRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/calendar/my-personal-schedules/{created.Id:D}");
        missingRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var missingResponse = await client.SendAsync(missingRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, missingResponse.StatusCode);
    }

    private static async Task VerifyTimeRangeFilterAsync(
        HttpClient client,
        string accessToken,
        DateTimeOffset startAtUtc,
        DateTimeOffset endAtUtc,
        Guid scheduleId,
        CancellationToken cancellationToken)
    {
        var inside = await ListSchedulesAsync(
            client,
            accessToken,
            startAtUtc.AddMinutes(-30),
            endAtUtc.AddMinutes(30),
            cancellationToken);
        Assert.IsTrue(inside.Items.Any(item => item.Id == scheduleId));

        var outside = await ListSchedulesAsync(
            client,
            accessToken,
            endAtUtc.AddHours(1),
            endAtUtc.AddHours(2),
            cancellationToken);
        Assert.IsFalse(outside.Items.Any(item => item.Id == scheduleId));
    }

    private static async Task VerifyCrossUserNotFoundAsync(
        FullNetApiFactory factory,
        HttpClient client,
        string adminToken,
        Guid scheduleId,
        CancellationToken cancellationToken)
    {
        var otherUser = await factory.CreateHostIdentityAsync(
            $"calendar-other-{Guid.NewGuid():N}"[..24],
            [
                CalendarPermissions.Read,
                CalendarPermissions.Create,
                CalendarPermissions.Update,
                CalendarPermissions.Delete,
                CalendarPermissions.SetStatus,
            ],
            cancellationToken);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/calendar/my-personal-schedules/{scheduleId:D}");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            otherUser.AccessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            CalendarErrorCodes.PersonalScheduleNotFound,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyConcurrencyConflictAsync(
        HttpClient client,
        string accessToken,
        Guid scheduleId,
        int staleVersion,
        CancellationToken cancellationToken)
    {
        using var request = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/calendar/my-personal-schedules/{scheduleId:D}",
            accessToken,
            new UpdatePersonalScheduleRequest(
                "并发冲突",
                DateTimeOffset.UtcNow.AddHours(3),
                DateTimeOffset.UtcNow.AddHours(4),
                staleVersion));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            CalendarErrorCodes.PersonalScheduleConcurrencyConflict,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task<PersonalScheduleResponse> CreateScheduleAsync(
        HttpClient client,
        string accessToken,
        string content,
        DateTimeOffset startAtUtc,
        DateTimeOffset endAtUtc,
        CancellationToken cancellationToken)
    {
        using var request = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/calendar/my-personal-schedules",
            accessToken,
            new CreatePersonalScheduleRequest(content, startAtUtc, endAtUtc));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<PersonalScheduleResponse>(
            cancellationToken);
        Assert.IsNotNull(created);
        return created;
    }

    private static async Task<PersonalScheduleResponse> GetScheduleAsync(
        HttpClient client,
        string accessToken,
        Guid scheduleId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/calendar/my-personal-schedules/{scheduleId:D}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var schedule = await response.Content.ReadFromJsonAsync<PersonalScheduleResponse>(
            cancellationToken);
        Assert.IsNotNull(schedule);
        return schedule;
    }

    private static async Task<PersonalScheduleResponse> UpdateScheduleAsync(
        HttpClient client,
        string accessToken,
        Guid scheduleId,
        UpdatePersonalScheduleRequest body,
        CancellationToken cancellationToken)
    {
        using var request = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/calendar/my-personal-schedules/{scheduleId:D}",
            accessToken,
            body);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var schedule = await response.Content.ReadFromJsonAsync<PersonalScheduleResponse>(
            cancellationToken);
        Assert.IsNotNull(schedule);
        return schedule;
    }

    private static async Task<PersonalScheduleResponse> SetStatusAsync(
        HttpClient client,
        string accessToken,
        Guid scheduleId,
        SetPersonalScheduleStatusRequest body,
        CancellationToken cancellationToken)
    {
        using var request = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/calendar/my-personal-schedules/{scheduleId:D}/status",
            accessToken,
            body);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var schedule = await response.Content.ReadFromJsonAsync<PersonalScheduleResponse>(
            cancellationToken);
        Assert.IsNotNull(schedule);
        return schedule;
    }

    private static async Task<PagedPersonalScheduleResponses> ListSchedulesAsync(
        HttpClient client,
        string accessToken,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/calendar/my-personal-schedules?page=1&pageSize=20&fromUtc={Uri.EscapeDataString(fromUtc.ToString("O"))}&toUtc={Uri.EscapeDataString(toUtc.ToString("O"))}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<PagedPersonalScheduleResponses>(
            cancellationToken);
        Assert.IsNotNull(page);
        return page;
    }

    private sealed record PagedPersonalScheduleResponses(
        PersonalScheduleResponse[] Items,
        int Page,
        int PageSize,
        long Total);

    private static async Task<CurrentUserResponse> GetCurrentUserAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var currentUser = await response.Content.ReadFromJsonAsync<CurrentUserResponse>(
            cancellationToken);
        Assert.IsNotNull(currentUser);
        return currentUser;
    }

    private static async Task<string> LoginAsHostAdminAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var loginRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/login")
        {
            Content = JsonContent.Create(
                new LoginRequest("admin", FullNetApiFactory.TestPassword)),
        };
        loginRequest.Headers.Add("Origin", "http://localhost");
        using var loginResponse = await client.SendAsync(loginRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);
        var token = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>(
            cancellationToken);
        Assert.IsNotNull(token);
        return token.AccessToken;
    }

    private static HttpRequestMessage CreateBearerJsonRequest<TRequest>(
        HttpMethod method,
        string path,
        string accessToken,
        TRequest body)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken);
        return request;
    }
}
