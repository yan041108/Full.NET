using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Ai.Contracts;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>两个独立浏览器操作者使用真实可读来源和本人库，避免跨标签/后台刷新竞争污染验收。</summary>
internal static class KnowledgeEvaluationRunBrowserFixture
{
    internal static readonly string[] Permissions=["identity.navigation.read","platform.dashboard.read",AiKnowledgePermissions.Create,AiKnowledgePermissions.Read,AiKnowledgePermissions.PolicyUpdate,AiKnowledgePermissions.MembersUpdate,
        AiKnowledgeDocumentPermissions.Read,AiKnowledgeDocumentPermissions.SourceRead,AiKnowledgeDocumentPermissions.IndexRead,AiKnowledgeSearchPermissions.Read,AiKnowledgeSearchPermissions.Compare,AiKnowledgeSearchPermissions.Refine,
        AiKnowledgeEvaluationPermissions.Read,AiKnowledgeEvaluationPermissions.Create,AiKnowledgeEvaluationPermissions.Append,AiKnowledgeEvaluationPermissions.Freeze,
        AiKnowledgeEvaluationRunPermissions.Read,AiKnowledgeEvaluationRunPermissions.Plan,AiKnowledgeEvaluationRunPermissions.Create,AiKnowledgeEvaluationRunPermissions.Execute,AiKnowledgeEvaluationRunPermissions.Skip,AiKnowledgeEvaluationRunPermissions.CloseUnknown,AiKnowledgeEvaluationRunPermissions.Review,
        AiKnowledgeFinancePermissions.Read,AiKnowledgeOperationsPermissions.Read,AiKnowledgeAnswerFeedbackReviewPermissions.Read];
    internal static async Task VerifyAsync(HttpClient client,string bridge,IReadOnlyList<(string Username,Guid UserId)> users,Guid sourceKnowledge,string sourceName,
        Guid embedding,int embeddingVersion,Guid processing,int processingVersion,Guid reviewerId,string reviewer,Func<Task> disableSource)
    {
        Directory.CreateDirectory(bridge);var rows=new List<object>();
        foreach(var (username,_) in users)
        {
            var token=await IntegrationTestAuthHelper.LoginAsHostUserAsync(client,username);var title=username.Contains("desktop",StringComparison.Ordinal)?"桌面实际运行":"移动实际运行";
            var knowledge=await Send<AiKnowledgeBaseResponse>(HttpMethod.Post,"/api/v1/ai/knowledge-bases",token,new CreateAiKnowledgeBaseRequest(title,null),HttpStatusCode.Created);
            var root=$"/api/v1/ai/knowledge-bases/{knowledge.Id}";
            knowledge=await Send<AiKnowledgeBaseResponse>(HttpMethod.Put,root+"/policy",token,new UpdateAiKnowledgePolicyRequest("internal",embedding,embeddingVersion,processing,processingVersion,knowledge.Version));
            await Send<AiKnowledgeMembersResponse>(HttpMethod.Put,root+"/members",token,new SetAiKnowledgeMembersRequest([reviewerId],knowledge.Version));
            var sample=new AiFeedbackEvaluationSample("zh-CN","明确 补写","浏览器人工参考","浏览器人工证据",false);
            var set=(await Send<AiKnowledgeEvaluationMutation>(HttpMethod.Post,root+"/evaluation-sets",token,new SaveAiKnowledgeEvaluationSet(Guid.CreateVersion7(),null,0,title,
                [new(null,sample,"浏览器独立人工依据",null),new(null,sample with{Question="人工事实"},"第二项人工依据",null)],true,true))).Workspace!;
            set=(await Send<AiKnowledgeEvaluationMutation>(HttpMethod.Post,root+$"/evaluation-sets/{set.Id}/approvals",reviewer,new ReviewAiKnowledgeEvaluationSet(Guid.CreateVersion7(),set.Revision.Id,set.Version,"approved",true))).Workspace!;
            rows.Add(new{username,knowledgeBaseId=knowledge.Id,knowledgeName=title,setId=set.Id,setName=title});
        }
        await File.WriteAllTextAsync(Path.Combine(bridge,"ready-writing.json"),JsonSerializer.Serialize(new{password=FullNetApiFactory.TestPassword,sourceKnowledgeBaseId=sourceKnowledge,sourceName,users=rows}));
        File.Move(Path.Combine(bridge,"ready-writing.json"),Path.Combine(bridge,"ready.json"));
        await Wait("advance.json","advance");await disableSource();await File.WriteAllTextAsync(Path.Combine(bridge,"replaced.json"),"{}");await Wait("complete.json","passed");
        async Task Wait(string file,string field)
        {using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(12));while(!File.Exists(Path.Combine(bridge,file)))await Task.Delay(250,timeout.Token);using var json=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(bridge,file),timeout.Token));Assert.IsTrue(json.RootElement.GetProperty(field).GetBoolean());}
        async Task<T> Send<T>(HttpMethod method,string url,string token,object body,HttpStatusCode expected=HttpStatusCode.OK)
        {using var request=new HttpRequestMessage(method,url){Content=JsonContent.Create(body,body.GetType())};request.Headers.Authorization=new("Bearer",token);using var response=await client.SendAsync(request);var text=await response.Content.ReadAsStringAsync();Assert.AreEqual(expected,response.StatusCode,text);return JsonSerializer.Deserialize<T>(text,new JsonSerializerOptions(JsonSerializerDefaults.Web))!;}
    }
}
