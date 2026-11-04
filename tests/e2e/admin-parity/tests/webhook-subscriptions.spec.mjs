import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import path from 'node:path';
const id='019bc2b1-2a40-7cc3-8992-a80de51bf299', second='019bc2b1-2a40-7cc3-8992-a80de51bf298';
const subscription={id,tenantId:id,eventType:'fullnet.workflow.instance.completed',targetUrl:'https://example.com/hooks',isActive:true,version:1};
const json=(route,value)=>route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(value)});
async function boot(page,permissions=['webhooks.subscriptions.read','webhooks.subscriptions.manage','webhooks.subscriptions.set_status']) {
 await page.setViewportSize({width:Number(process.env.FULLNET_ADMIN_PARITY_VIEWPORT_WIDTH??1440),height:1000});
 await page.addInitScript(()=>localStorage.setItem('fullnet.admin.locale','zh-CN'));await page.route('**/api/v1/**',route=>route.fulfill({status:404}));
 await page.route('**/api/v1/auth/refresh',route=>json(route,{accessToken:'fixture',tokenType:'Bearer',expiresAtUtc:'2099-01-01T00:00:00Z'}));
 await page.route('**/api/v1/me',route=>json(route,{id,username:'fixture',displayName:'Tenant',tenantId:id,actorScope:`tenant:${id.replaceAll('-','')}`,scope:`tenant:${id.replaceAll('-','')}`,isSuperAdministrator:false,passwordChangeRequired:false,permissions,sessionId:id,preferredLocale:'zh-CN',profileVersion:1}));
 await page.route('**/api/v1/navigation',route=>json(route,[{id:'fixture',parentId:null,routeName:'webhook-subscriptions',path:'/webhooks/subscriptions',componentKey:'webhook-subscriptions',title:'Webhook 订阅',caption:'',icon:'link',order:1,requiredPermission:'webhooks.subscriptions.read',children:[]}]));
 const state={rows:[{...subscription}],lists:0,creates:0,statuses:0};
 await page.route('**/api/v1/webhooks/subscriptions',route=>{if(route.request().method()==='GET'){state.lists++;return json(route,state.rows);}state.creates++;state.rows.push({...subscription,id:second});return json(route,state.rows.at(-1));});
 await page.route(`**/api/v1/webhooks/subscriptions/${id}/status`,route=>{state.statuses++;const body=route.request().postDataJSON();expect(body.version).toBe(state.rows[0].version);state.rows[0]={...state.rows[0],isActive:body.isActive,version:body.version+1};return json(route,state.rows[0]);});
 await page.goto('/#/webhooks/subscriptions');await expect(page.locator('.el-table')).toContainText(subscription.targetUrl);return state;
}
test('订阅暂停与恢复明确确认，旧历史保留且移动弹窗可见',async({page},info)=>{
 const state=await boot(page);await page.getByTestId('webhook-status').click();const dialog=page.getByRole('dialog');await expect(dialog).toContainText('已发出的 HTTP 请求');expect(state.statuses).toBe(0);
 const box=await dialog.boundingBox();expect(box.x).toBeGreaterThanOrEqual(0);expect(box.x+box.width).toBeLessThanOrEqual(page.viewportSize().width);
 expect((await new AxeBuilder({page}).include('[role="dialog"]').withTags(['wcag2a','wcag2aa','wcag21a','wcag21aa']).analyze()).violations).toEqual([]);
 await page.screenshot({path:path.resolve(info.outputDir,'..','webhook-status-confirm.png'),fullPage:true,animations:'disabled'});
 await page.getByTestId('webhook-status-confirm').click();await expect(page.getByTestId('webhook-status')).toHaveText('恢复订阅');expect(state.statuses).toBe(1);
 await page.getByTestId('webhook-status').click();await page.getByTestId('webhook-status-confirm').click();await expect(page.getByTestId('webhook-status')).toHaveText('暂停订阅');expect(state.statuses).toBe(2);expect(state.rows).toHaveLength(1);
});
test('创建不回显签名密钥，成功后清空并保留回执',async({page},info)=>{
 const state=await boot(page);await page.getByTestId('webhook-create').click();await page.getByTestId('webhook-target').fill(subscription.targetUrl);await page.getByTestId('webhook-secret').fill('browser-independent-secret');
 // 先完成有限入场动画再检查对比度，避免把过渡透明度当作静态颜色。
 await page.screenshot({path:path.resolve(info.outputDir,'..','webhook-create.png'),fullPage:true,animations:'disabled'});
 expect((await new AxeBuilder({page}).include('[role="dialog"]').withTags(['wcag2a','wcag2aa','wcag21a','wcag21aa']).analyze()).violations).toEqual([]);
 await page.getByTestId('webhook-create-submit').click();await expect(page.getByRole('dialog')).toHaveCount(0);await expect(page.getByTestId('webhook-receipt')).toContainText(second);expect(state.creates).toBe(1);
 await expect(page.locator('body')).not.toContainText('browser-independent-secret');await page.getByTestId('webhook-create').click();await expect(page.getByTestId('webhook-secret')).toHaveValue('');
});
test('只读订阅没有创建与状态入口',async({page})=>{
 const state=await boot(page,['webhooks.subscriptions.read']);await expect(page.getByTestId('webhook-create')).toHaveCount(0);await expect(page.getByTestId('webhook-status')).toHaveCount(0);expect(state.creates).toBe(0);expect(state.statuses).toBe(0);
});
test('状态响应丢失须人工重读，不自动重放',async({page})=>{
 const state=await boot(page);let writes=0;
 await page.route(`**/api/v1/webhooks/subscriptions/${id}/status`,route=>{writes++;return route.abort('failed');});
 await page.getByTestId('webhook-status').click();await page.getByTestId('webhook-status-confirm').click();await expect(page.getByTestId('webhook-unconfirmed')).toBeVisible();
 await page.getByRole('dialog').getByRole('button',{name:'取消',exact:true}).click();await expect(page.getByTestId('webhook-status')).toBeDisabled();expect(writes).toBe(1);
 await page.getByTestId('webhook-refresh').click();await expect(page.getByTestId('webhook-status')).toBeEnabled();expect(writes).toBe(1);expect(state.lists).toBe(2);
});
test('关闭在途创建后密钥清空，迟到响应不能误报成功',async({page})=>{
 const state=await boot(page);let release,finished=false,started=false;const pending=new Promise(resolve=>{release=resolve;});
 await page.route('**/api/v1/webhooks/subscriptions',async route=>{if(route.request().method()==='GET')return json(route,state.rows);started=true;await pending;await json(route,{...subscription,id:second}).catch(()=>{});finished=true;});
 await page.getByTestId('webhook-create').click();await page.getByTestId('webhook-target').fill(subscription.targetUrl);await page.getByTestId('webhook-secret').fill('browser-independent-secret');await page.getByTestId('webhook-create-submit').click();await expect.poll(()=>started).toBe(true);
 await page.getByRole('dialog').getByRole('button',{name:'取消',exact:true}).click();release();await expect.poll(()=>finished).toBe(true);await expect(page.getByTestId('webhook-unconfirmed')).toBeVisible();await expect(page.locator('.el-message--success')).toHaveCount(0);
 await page.getByTestId('webhook-create').click();await expect(page.getByTestId('webhook-secret')).toHaveValue('');await expect(page.getByTestId('webhook-create-submit')).toHaveCount(0);
});
