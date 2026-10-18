import {expect,test} from '@playwright/test';
const id=n=>`01900000-0000-7000-8000-${String(n).padStart(12,'0')}`;
async function setup(page, publish = true, workflow = false) {
  let revision=1, activeVersionId=null, wasPublished=false, lastRun;
  const promptId=id(1), versionId=id(2), modelId=id(3);
  if (workflow) { activeVersionId=versionId;wasPublished=true; }
  const execution={policyVersion:2,definitionKey:'fullnet-chat-rename-workflow-v1',definitionVersion:2,definitionDigest:'b'.repeat(64),inputSchemaKey:'ai.workflow.chat_rename.input.v2',outputSchemaKey:'ai.workflow.chat_rename.result.v2',allowedToolNames:['ai.chat.sessions.list','ai.chat.sessions.rename'],retrievalStrategyKey:'none'};
  const head=()=>({id:promptId,name:'浏览器草稿',revision,lastVersion:1,activeVersionId,epoch:0});
  const version=()=>({id:versionId,promptId,version:1,body:{modelConfigId:modelId,languageTag:'zh-CN',template:workflow?'{{input}}':'浏览器 {{input}}',inputTokenLimit:100,outputTokenLimit:100,...(workflow?{execution}:{})},modelConfigVersion:1,providerKey:'ollama',modelId:'model',bodyHash:'a'.repeat(64),wasPublished,createdAtUtc:'2026-10-09T00:00:00Z',definitionKey:'fullnet-single-text-v1',definitionVersion:1,policyVersion:1,inputSchemaKey:'ai.prompt.text.v1',outputSchemaKey:'ai.prompt.text.v1',allowedToolNames:[],retrievalStrategyKey:'none',...(workflow?execution:{})});
  const json=(r,body,status=200)=>r.fulfill({status,contentType:'application/json',body:JSON.stringify(body)});
  await page.addInitScript(()=>localStorage.setItem('fullnet.admin.locale','zh-CN'));
  await page.route('**/api/v1/auth/refresh',r=>json(r,{accessToken:'prompt-test',tokenType:'Bearer',expiresAtUtc:new Date(Date.now()+3600000).toISOString()}));
  await page.route('**/api/v1/me',r=>json(r,{id:id(4),username:'prompt-user',displayName:'Prompt user',actorScope:'host',scope:'host',isSuperAdministrator:false,tenantId:null,permissions:['identity.navigation.read','ai.agent_runs.read','ai.agent_runs.create','ai.prompts.read','ai.prompts.use',...(publish?['ai.prompts.publish']:[])],sessionId:id(6),preferredLocale:'zh-CN',profileVersion:1,passwordChangeRequired:false}));
  await page.route('**/api/v1/navigation',r=>json(r,[{id:'runs',parentId:null,routeName:'ai-agent-runs',path:'/ai/agent-runs',componentKey:'ai-agent-runs',title:'Agent运行',caption:'Agent运行',icon:'chat',order:1,requiredPermission:'ai.agent_runs.read',children:[]} ]));
  await page.route('**/api/v1/tenancy/available',r=>json(r,[]));
  await page.route('**/api/v1/notifications/my-inbox-messages/unread-count',r=>json(r,{unreadCount:0}));
  await page.route('**/api/v1/ai/prompts?*',r=>json(r,{items:[head()],page:1,pageSize:100,total:1}));
  await page.route('**/api/v1/ai/prompts/execution-strategies',r=>json(r,workflow?[execution]:[]));
  await page.route(`**/api/v1/ai/prompts/${promptId}/versions?*`,r=>json(r,{items:[version()],page:1,pageSize:100,total:1}));
  await page.route(`**/api/v1/ai/prompts/${promptId}/publish`,r=>{expect(r.request().postDataJSON()).toEqual({revision:1,versionId});revision=2;activeVersionId=versionId;wasPublished=true;return json(r,head());});
  await page.route('**/api/v1/ai/agent/runs',r=>{lastRun=r.request().postDataJSON();return json(r,{runId:id(5)},202);});
  await page.route(`**/api/v1/ai/agent/runs/${id(5)}`,r=>json(r,{id:id(5),statusKey:'queued',definitionKey:workflow?execution.definitionKey:'fullnet-single-text-v1',definitionVersion:workflow?execution.definitionVersion:1,deadlineAtUtc:'2026-10-09T01:00:00Z',createdAtUtc:'2026-10-09T00:00:00Z',updatedAtUtc:'2026-10-09T00:00:00Z'}));
  await page.route(`**/api/v1/ai/agent/runs/${id(5)}/events/stream`,r=>r.fulfill({status:200,contentType:'text/event-stream',body:''}));
  await page.goto('/ai/agent-runs'); await page.getByTestId('prompt-refresh').click();await page.getByTestId('prompt-catalog').selectOption(promptId);
  return {run:()=>lastRun,versionId,modelId};
}
test('显式确认发布后将精确版本用于纯文本 Run',async({page})=>{
  const state=await setup(page);await page.getByTestId('prompt-publish').click();
  await expect(page.getByRole('dialog')).toContainText('浏览器草稿');
  await expect(page.getByTestId('prompt-use')).toHaveCount(0);
  await page.getByRole('dialog').getByRole('button').last().click();
  await page.getByTestId('prompt-use').click();
  await page.getByTestId('ai-agent-runs-create-prompt').fill('实际输入');
  await page.getByTestId('ai-agent-runs-create').click();
  await expect.poll(()=>state.run()).toMatchObject({promptVersionId:state.versionId,modelConfigId:state.modelId,definitionKey:'fullnet-single-text-v1',inputTokenLimit:100,outputTokenLimit:100});
});
test('只读目录不展示发布与回滚动作',async({page})=>{
  await setup(page,false);await expect(page.getByTestId('prompt-version')).toContainText('草稿');
  await expect(page.getByTestId('prompt-publish')).toHaveCount(0);await expect(page.getByTestId('prompt-rollback')).toHaveCount(0);
});
test('显式工作流版本锁定静态定义并提交空文本输入',async({page})=>{
  const state=await setup(page,true,true);await page.getByTestId('prompt-use').click();
  await expect(page.getByTestId('ai-agent-runs-create-prompt')).toBeDisabled();
  await page.getByTestId('ai-agent-runs-create').click();
  await expect.poll(()=>state.run()).toMatchObject({promptVersionId:state.versionId,modelConfigId:state.modelId,definitionKey:'fullnet-chat-rename-workflow-v1',prompt:'',inputTokenLimit:100,outputTokenLimit:100});
});
