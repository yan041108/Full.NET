import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import fs from 'node:fs/promises';
import path from 'node:path';

const run='01900000-0000-7000-8000-000000000701';
const thread='01900000-0000-7000-8000-000000000702';
const privateDefinition='PRIVATE_PROTOCOL_RUN_METADATA';
const expiry='2099-11-01T00:00:00Z';

// 受控HTTP仅验证真实Vue的流内撤权展示；真实会话、Native、双库和SDK另有独立证据。
for(const viewport of [{width:1440,height:1000},{width:390,height:844}]){
  test(`流内撤权清空运行及事件 ${viewport.width}`,async({page})=>{
    await page.setViewportSize(viewport);
    const screenshots=process.env.FULLNET_A04_UI_SCREENSHOTS;
    if(!screenshots)throw Error('Provide a dedicated temporary screenshot directory.');
    await fs.mkdir(screenshots,{recursive:true});
    const json=(route,body,status=200)=>route.fulfill({status,contentType:'application/json',body:JSON.stringify(body)});
    await page.addInitScript(()=>localStorage.setItem('fullnet.admin.locale','zh-CN'));
    await page.route('**/api/v1/**',route=>json(route,{type:'about:blank',status:503,code:'fixture.unexpected_request'},503));
    await page.route('**/api/v1/auth/refresh',route=>json(route,{accessToken:'agui-controlled-ui-only',tokenType:'Bearer',expiresAtUtc:expiry}));
    await page.route('**/api/v1/me',route=>json(route,{id:thread,username:'agui-controlled',displayName:'协议界面验收',tenantId:null,actorScope:'host',scope:'host',isSuperAdministrator:false,
      permissions:['identity.navigation.read','ai.agent_runs.read'],sessionId:thread,preferredLocale:'zh-CN',profileVersion:1,passwordChangeRequired:false}));
    await page.route('**/api/v1/navigation',route=>json(route,[{id:'runs',parentId:null,routeName:'ai-agent-runs',path:'/ai/agent-runs',componentKey:'ai-agent-runs',title:'Agent运行',caption:'Agent运行',icon:'chat',order:1,requiredPermission:'ai.agent_runs.read',children:[]}]));
    await page.route('**/api/v1/tenancy/available',route=>json(route,[]));
    await page.route('**/api/v1/notifications/my-inbox-messages/unread-count',route=>json(route,{unreadCount:0}));
    await page.route('**/api/v1/ai/model-configs?*',route=>json(route,{items:[],page:1,pageSize:100,total:0}));
    let reads=0;const methods=[];
    await page.route(`**/api/v1/ai/agent/runs/${run}`,route=>{
      ++reads;methods.push(route.request().method());return json(route,{id:run,statusKey:'running',definitionKey:privateDefinition,definitionVersion:1,deadlineAtUtc:expiry,createdAtUtc:expiry,updatedAtUtc:expiry});
    });
    let release;let arrived;
    const gate=new Promise(resolve=>{release=resolve;});const streamRequested=new Promise(resolve=>{arrived=resolve;});
    await page.route(`**/api/v1/ai/agent/runs/${run}/events/stream*`,async route=>{
      methods.push(route.request().method());arrived();await gate;
      const frames=[{type:'RUN_STARTED',runId:run,threadId:thread},
        {type:'STATE_SNAPSHOT',snapshot:{statusKey:'running',definitionKey:privateDefinition,budget:null,steps:[]}},
        {type:'CUSTOM',name:'run.protocol_fixture',value:{runId:run,sequence:1,payloadVersion:1,payload:'PRIVATE_OWNED_EVENT'}},
        {type:'RUN_ERROR',code:'authorization.permission_denied',message:'authorization.permission_denied'}];
      await route.fulfill({status:200,contentType:'text/event-stream',body:frames.map(event=>`event: ${event.type}\ndata: ${JSON.stringify(event)}\n\n`).join('')});
    });
    const pageErrors=[];page.on('pageerror',error=>pageErrors.push(error.message));
    try{
      await page.goto('/ai/agent-runs');
      await page.getByTestId('ai-agent-runs-id').fill(run);
      const load=page.getByTestId('ai-agent-runs-load');await load.focus();await load.press('Enter');
      await streamRequested;await expect(page.getByText(privateDefinition,{exact:true})).toBeVisible();
      await page.screenshot({path:path.join(screenshots,`agui-${viewport.width}-before.png`),fullPage:true});
      release();
      await expect(page.getByTestId('ai-agent-runs-id')).toHaveValue('');
      await expect(page.getByText(privateDefinition,{exact:true})).toHaveCount(0);
      await expect(page.getByText('PRIVATE_OWNED_EVENT',{exact:false})).toHaveCount(0);
      await expect(page.locator('.art-page-alert')).toBeVisible();
      await expect(page.locator('.ai-agent-runs-view')).toHaveAttribute('aria-busy','false');
      await load.focus();await expect(load).toBeFocused();
      expect(reads).toBe(1);expect(methods.every(method=>method==='GET')).toBe(true);
      expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
      const axe=await new AxeBuilder({page}).analyze();expect(axe.violations).toEqual([]);
      expect(pageErrors).toEqual([]);
      await page.screenshot({path:path.join(screenshots,`agui-${viewport.width}-after.png`),fullPage:true});
    }finally{release();}
  });
}
