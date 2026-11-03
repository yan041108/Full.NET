using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Calendar.Contracts;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>复用真实发布准备，只验新增业务输入、独立引用、完整持久历史与收费前后拒绝。</summary>
internal static class PersonalScheduleAnswerAssertions
{
    internal static async Task VerifyAsync(HttpClient client, DbConnection db, string token, Guid knowledge, Guid embedding, Guid generation,
        Action? preferBusinessCitation, Func<string[]>? streamingInputs)
    {
        const string calendar = "/api/v1/calendar/my-personal-schedules";
        var time = DateTimeOffset.UtcNow;
        var first = await Send<PersonalScheduleResponse>(HttpMethod.Post, calendar, new CreatePersonalScheduleRequest("新增日程上下文", time, time.AddHours(1)), HttpStatusCode.Created);
        var uncited = await Send<PersonalScheduleResponse>(HttpMethod.Post, calendar, new CreatePersonalScheduleRequest("未引用但实际输入的日程", time, time), HttpStatusCode.Created);
        var preview = await Send<AiPersonalScheduleSourcePreview>(HttpMethod.Post, "/api/v1/ai/personal-schedule-assistant/sources/preview", new PreviewAiPersonalScheduleSources([first.Id, uncited.Id]));
        var chat = await Send<AiChatSessionResponse>(HttpMethod.Post, "/api/v1/ai/chat/sessions", new CreateAiChatSessionRequest(generation, "日程混合知识问答"), HttpStatusCode.Created);
        var path = $"/api/v1/ai/chat/sessions/{chat.Id}/knowledge/ask";
        var ask = new AskAiKnowledgeRequest([knowledge], embedding, "结合知识与两项日程回答", PersonalSchedules: preview.Sources.Select(value => new AiPersonalScheduleSourceSelection(value.Id, value.Version, value.ProjectionHash)).ToArray());
        var before = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget");
        var files = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_files_file");
        var answer = await Send<AskAiKnowledgeResponse>(HttpMethod.Post, path, ask);
        Assert.IsFalse(answer.Refused); Assert.IsFalse(string.IsNullOrWhiteSpace(answer.Message.Content));
        Assert.AreEqual(1, answer.Message.Citations.Count + answer.Message.PersonalScheduleCitations.Count);
        var json = await db.QuerySingleAsync<string>("SELECT KnowledgeLineage FROM fn_ai_chat_message WHERE Id=@Id", new { answer.Message.Id });
        using (var encoded = JsonDocument.Parse(json))
        {
            Assert.AreEqual(8, encoded.RootElement.GetProperty("version").GetInt32());
            Assert.AreEqual(2, encoded.RootElement.GetProperty("personalSchedules").GetArrayLength());
            Assert.IsFalse(json.Contains("未引用但实际输入", StringComparison.Ordinal));
        }
        if (preferBusinessCitation is not null)
        {
            preferBusinessCitation(); var cited = await Send<AskAiKnowledgeResponse>(HttpMethod.Post, path, ask);
            Assert.HasCount(0, cited.Message.Citations); Assert.HasCount(1, cited.Message.PersonalScheduleCitations);
            Assert.AreEqual(first.Id, cited.Message.PersonalScheduleCitations[0].ScheduleId);
            Assert.AreEqual(preview.Sources[0].ProjectionHash, cited.Message.PersonalScheduleCitations[0].ProjectionHash);
            using var continuation = Request(HttpMethod.Post, $"/api/v1/ai/chat/sessions/{chat.Id}/messages/stream", new StreamAiChatMessageRequest("继续普通聊天，来源仍可读"));
            using var streamed = await client.SendAsync(continuation); Assert.AreEqual(HttpStatusCode.OK, streamed.StatusCode);
            var events = await streamed.Content.ReadAsStringAsync(); StringAssert.Contains(events, "event: delta"); StringAssert.Contains(events, "event: done");
            Assert.IsFalse(events.Contains("event: error", StringComparison.Ordinal));
            Assert.IsNotNull(streamingInputs); var actualInput = string.Join("\n", streamingInputs());
            Assert.IsFalse(actualInput.Contains(first.Content, StringComparison.Ordinal)); Assert.IsFalse(actualInput.Contains(uncited.Content, StringComparison.Ordinal));
            Assert.IsFalse(actualInput.Contains(answer.Message.Content, StringComparison.Ordinal));
        }
        var reloaded = await Send<AiChatSessionResponse>(HttpMethod.Get, $"/api/v1/ai/chat/sessions/{chat.Id}");
        Assert.AreEqual(answer.Message.Content, reloaded.Messages.Single(value => value.Id == answer.Message.Id).Content);
        Assert.AreEqual(before + (preferBusinessCitation is null ? 2 : 5), await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget"));
        Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget WHERE SettledAtUtc IS NULL"));
        // 替换未引用输入，历史与继续对话都不得沿用旧允许；旧期望问答不增加模型费用。
        await Send<PersonalScheduleResponse>(HttpMethod.Put, calendar + "/" + uncited.Id,
            new UpdatePersonalScheduleRequest("已改变但未引用的日程", time, time, uncited.Version));
        reloaded = await Send<AiChatSessionResponse>(HttpMethod.Get, $"/api/v1/ai/chat/sessions/{chat.Id}");
        foreach (var message in reloaded.Messages.Where(value => value.IsKnowledgeAnswer))
        {
            Assert.AreEqual("", message.Content); Assert.AreEqual(AiErrorCodes.KnowledgeAnswerUnavailable, message.KnowledgeUnavailableCode);
            Assert.HasCount(0, message.Citations); Assert.HasCount(0, message.PersonalScheduleCitations);
        }
        var guarded = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget");
        await Expect(HttpMethod.Post, path, ask, HttpStatusCode.Conflict);
        using (var continuation = Request(HttpMethod.Post, $"/api/v1/ai/chat/sessions/{chat.Id}/messages/stream", new StreamAiChatMessageRequest("继续对话")))
        using (var rejected = await client.SendAsync(continuation))
        {
            // 新门禁在启动传输和预留前拒绝，不以流内模型失败或零用量预留冒充来源核证。
            Assert.AreEqual(HttpStatusCode.Conflict, rejected.StatusCode); using var problem = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
            Assert.AreEqual(AiErrorCodes.KnowledgeAnswerUnavailable, problem.RootElement.GetProperty("code").GetString());
        }
        Assert.AreEqual(guarded, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget"));
        Assert.AreEqual(files, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_files_file"));

        async Task<T> Send<T>(HttpMethod method, string url, object? body = null, HttpStatusCode expected = HttpStatusCode.OK)
        {
            using var request = Request(method, url, body); using var response = await client.SendAsync(request);
            Assert.AreEqual(expected, response.StatusCode, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<T>())!;
        }
        async Task Expect(HttpMethod method, string url, object body, HttpStatusCode expected)
        {
            using var request = Request(method, url, body); using var response = await client.SendAsync(request);
            Assert.AreEqual(expected, response.StatusCode, await response.Content.ReadAsStringAsync());
        }
        HttpRequestMessage Request(HttpMethod method, string url, object? body)
        {
            var request = new HttpRequestMessage(method, url); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body is not null) request.Content = JsonContent.Create(body); return request;
        }
    }
}
