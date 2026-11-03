using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Calendar.Contracts;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>独立本人账号消费同一真实发布；页面登录后后台不得重登或复用其过期授权。</summary>
internal static class PersonalScheduleAnswerBrowserFixture
{
    internal static readonly string[] Permissions = ["identity.navigation.read", "platform.dashboard.read", AiModelPermissions.Read,
        AiChatPermissions.Create, AiChatPermissions.Read, AiChatPermissions.Send, AiKnowledgePermissions.Read,
        AiKnowledgeDocumentPermissions.Read, AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.IndexRead,
        CalendarPermissions.Read, CalendarPermissions.Create, CalendarPermissions.Update, PersonalScheduleKnowledgePermissions.Read];

    internal static async Task VerifyAsync(HttpClient client, DbConnection db, string bridge, IReadOnlyList<(string Username, Guid UserId)> users,
        Guid knowledge, string knowledgeName, Guid embedding, Guid generation)
    {
        if (Directory.Exists(bridge)) throw new InvalidOperationException("不能采用旧日程问答桥接。");
        Directory.CreateDirectory(bridge); var ready = new List<object>(); var ids = new List<Guid>(); var chats = new List<Guid>();
        foreach (var user in users)
        {
            var token = await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, user.Username);
            var start = DateTimeOffset.UtcNow.AddMinutes(2);
            var schedule = await Send<PersonalScheduleResponse>("/api/v1/calendar/my-personal-schedules",
                new CreatePersonalScheduleRequest("本人混合问答 · <script>只显示文本</script> · " + user.Username, start, start.AddMinutes(30)), token);
            var chat = await Send<AiChatSessionResponse>("/api/v1/ai/chat/sessions", new CreateAiChatSessionRequest(generation, "本人日程与知识问答"), token);
            ids.Add(schedule.Id); chats.Add(chat.Id);
            ready.Add(new { username = user.Username, scheduleId = schedule.Id, content = schedule.Content, version = schedule.Version,
                startAtUtc = schedule.StartAtUtc, endAtUtc = schedule.EndAtUtc, sessionId = chat.Id, modelName = chat.ModelName });
        }
        var before = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget");
        await File.WriteAllTextAsync(Path.Combine(bridge, "ready.writing.json"), JsonSerializer.Serialize(new { password = FullNetApiFactory.TestPassword,
            knowledgeBaseId = knowledge, knowledgeName, embeddingModelConfigId = embedding, users = ready }));
        File.Move(Path.Combine(bridge, "ready.writing.json"), Path.Combine(bridge, "ready.json"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await Wait("complete-1440.json"); await Wait("complete-390.json");
        Assert.AreEqual(before + 4, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget"));
        foreach (var id in ids)
            Assert.AreEqual(2, await db.QuerySingleAsync<int>("SELECT Version FROM fn_calendar_personal_schedule WHERE Id=@Id", new { Id = id }));
        Assert.AreEqual(2, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_chat_message WHERE SessionId IN @Ids AND IsKnowledgeAnswer=1 AND KnowledgeLineage IS NOT NULL", new { Ids = chats.ToArray() }));

        async Task<T> Send<T>(string path, object body, string token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) }; request.Headers.Authorization = new("Bearer", token);
            using var response = await client.SendAsync(request); Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<T>())!;
        }
        async Task Wait(string name)
        {
            while (!File.Exists(Path.Combine(bridge, name)))
            {
                if (File.Exists(Path.Combine(bridge, "failed.json"))) Assert.Fail("正式页面失败，原结果必须保留。");
                await Task.Delay(250, timeout.Token);
            }
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(bridge, name), timeout.Token));
            Assert.IsTrue(document.RootElement.GetProperty("passed").GetBoolean());
        }
    }
}
