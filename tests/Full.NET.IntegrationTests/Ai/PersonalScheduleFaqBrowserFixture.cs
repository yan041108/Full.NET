using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Calendar.Contracts;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>两个独立本人账号走正式FAQ页面；页面登录后只读数据库，不后台重登竞争会话。</summary>
internal static class PersonalScheduleFaqBrowserFixture
{
    internal static readonly string[] Permissions=["identity.navigation.read","platform.dashboard.read",AiModelPermissions.Read,
        AiKnowledgePermissions.Read,AiKnowledgePermissions.Create,AiKnowledgePermissions.PolicyUpdate,AiKnowledgeSearchPermissions.Read,
        AiKnowledgeDocumentPermissions.Read,AiKnowledgeDocumentPermissions.SourceRead,AiKnowledgeDocumentPermissions.IndexRead,
        AiKnowledgeFaqPermissions.Read,AiKnowledgeFaqPermissions.Write,AiKnowledgeFaqPermissions.Export,AiKnowledgeFaqPermissions.Import,
        CalendarPermissions.Read,CalendarPermissions.Create,CalendarPermissions.Update,PersonalScheduleKnowledgePermissions.Read];
    internal static async Task VerifyAsync(HttpClient client,DbConnection db,string bridge,IReadOnlyList<(string Username,Guid UserId)> users,
        string sourceKnowledgeName,Guid embedding,int embeddingVersion,Guid generation,int generationVersion)
    {
        if(Directory.Exists(bridge))throw new InvalidOperationException("不能采用旧业务FAQ桥接。");Directory.CreateDirectory(bridge);
        var ready=new List<object>();var schedules=new List<Guid>();
        foreach(var user in users)
        {
            var token=await IntegrationTestAuthHelper.LoginAsHostUserAsync(client,user.Username);var now=DateTimeOffset.UtcNow;
            var personal=await Send<PersonalScheduleResponse>(HttpMethod.Post,"/api/v1/calendar/my-personal-schedules",token,
                new CreatePersonalScheduleRequest("本人FAQ来源 <script>只显示文本</script> "+user.Username,now,now),HttpStatusCode.Created);
            var knowledge=await Send<AiKnowledgeBaseResponse>(HttpMethod.Post,"/api/v1/ai/knowledge-bases",token,new CreateAiKnowledgeBaseRequest("本人FAQ目标 "+user.Username,null),HttpStatusCode.Created);
            knowledge=await Send<AiKnowledgeBaseResponse>(HttpMethod.Put,$"/api/v1/ai/knowledge-bases/{knowledge.Id}/policy",token,
                new UpdateAiKnowledgePolicyRequest("restricted",embedding,embeddingVersion,generation,generationVersion,knowledge.Version));
            schedules.Add(personal.Id);ready.Add(new{username=user.Username,userId=user.UserId,knowledgeBaseId=knowledge.Id,knowledgeName=knowledge.Name,
                scheduleId=personal.Id,version=personal.Version,content=personal.Content,startAtUtc=personal.StartAtUtc,endAtUtc=personal.EndAtUtc});
        }
        var fees=await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget");var files=await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_files_file");
        await File.WriteAllTextAsync(Path.Combine(bridge,"ready.writing.json"),JsonSerializer.Serialize(new{password=FullNetApiFactory.TestPassword,sourceKnowledgeName,users=ready}));
        File.Move(Path.Combine(bridge,"ready.writing.json"),Path.Combine(bridge,"ready.json"));
        using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(6));await Wait("complete-1440.json");await Wait("complete-390.json");
        Assert.AreEqual(fees,await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget"));Assert.AreEqual(files,await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_files_file"));
        foreach(var id in schedules)Assert.AreEqual(2,await db.QuerySingleAsync<int>("SELECT Version FROM fn_calendar_personal_schedule WHERE Id=@Id",new{Id=id}));
        Assert.AreEqual(2,await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_faq WHERE CreatedById IN @Ids",new{Ids=users.Select(user=>user.UserId).ToArray()}));
        async Task<T> Send<T>(HttpMethod method,string path,string token,object body,HttpStatusCode status=HttpStatusCode.OK)
        {using var message=new HttpRequestMessage(method,path){Content=JsonContent.Create(body)};message.Headers.Authorization=new("Bearer",token);using var response=await client.SendAsync(message);Assert.AreEqual(status,response.StatusCode,await response.Content.ReadAsStringAsync());return(await response.Content.ReadFromJsonAsync<T>())!;}
        async Task Wait(string name)
        {while(!File.Exists(Path.Combine(bridge,name))){if(File.Exists(Path.Combine(bridge,"failed.json")))Assert.Fail("正式业务FAQ页面失败，保留原结果。");await Task.Delay(250,timeout.Token);}using var result=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(bridge,name),timeout.Token));Assert.IsTrue(result.RootElement.GetProperty("passed").GetBoolean());}
    }
}
