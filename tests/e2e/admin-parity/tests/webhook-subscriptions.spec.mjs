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
 await expect(page.getByTestId('webhook-event')).toHaveAttribute('maxlength','128');await expect(page.getByTestId('webhook-secret')).toHaveAttribute('maxlength','128');
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

const deliveryA='019bc2b1-2a40-7cc3-8992-a80de51bf297',deliveryB='019bc2b1-2a40-7cc3-8992-a80de51bf296',eventId='019bc2b1-2a40-7cc3-8992-a80de51bf295';
const history={id:deliveryA,tenantId:id,subscriptionId:id,eventId,statusKey:'failed',attemptCount:2,nextAttemptAtUtc:null,lastErrorCode:'webhooks.delivery.http_error',lastHttpStatus:503,createdAtUtc:'2026-11-04T00:00:00Z',updatedAtUtc:'2026-11-04T00:01:00Z',version:3};
const historyRights=['webhooks.subscriptions.read','webhooks.deliveries.read'];
test('历史分页筛选与详情只读取原订阅，独立读取权限可用',async({page},info)=>{
 await boot(page,historyRights);let lists=0,details=0;const queries=[];
 await page.route(`**/api/v1/webhooks/subscriptions/${id}/deliveries?*`,route=>{lists++;const url=new URL(route.request().url());queries.push(url.search);const pageNumber=Number(url.searchParams.get('page'));return json(route,{items:[{...history,id:pageNumber===2?deliveryB:deliveryA}],page:pageNumber,pageSize:Number(url.searchParams.get('pageSize')),total:url.searchParams.has('statusKey')?1:21});});
 await page.route(`**/api/v1/webhooks/subscriptions/${id}/deliveries/${deliveryA}`,route=>{details++;return json(route,history);});
 await expect(page.getByTestId('webhook-create')).toHaveCount(0);await expect(page.getByTestId('webhook-status')).toHaveCount(0);await page.getByTestId('webhook-history-open').click();await expect.poll(()=>lists).toBe(1);
 await page.locator('.webhook-history__pages .btn-next').click();await expect.poll(()=>lists).toBe(2);expect(queries[1]).toContain('page=2');
 await page.getByTestId('webhook-history-filter').click();await page.getByRole('option',{name:'已失败',exact:true}).click();await expect.poll(()=>lists).toBe(3);expect(queries[2]).toContain('page=1');expect(queries[2]).toContain('statusKey=failed');
 await page.getByTestId('webhook-history-page-size').click();await page.getByRole('option',{name:'50 条/页',exact:true}).click();await expect.poll(()=>lists).toBe(4);expect(queries[3]).toContain('page=1');expect(queries[3]).toContain('pageSize=50');
 await page.getByTestId('webhook-history-detail').click();await expect(page.getByTestId('webhook-history-detail-panel')).toContainText('接收端返回 HTTP 503');await expect(page.getByTestId('webhook-history-detail-panel')).toContainText(deliveryA);expect(details).toBe(1);
 const dialog=page.getByRole('dialog');const box=await dialog.boundingBox();expect(box.x).toBeGreaterThanOrEqual(0);expect(box.x+box.width).toBeLessThanOrEqual(page.viewportSize().width);
 await page.screenshot({path:path.resolve(info.outputDir,'..','webhook-history.png'),fullPage:true,animations:'disabled'});
 expect((await new AxeBuilder({page}).include('[role="dialog"]').withTags(['wcag2a','wcag2aa','wcag21a','wcag21aa']).analyze()).violations).toEqual([]);
});
test('历史失败可人工重读，错误投递详情不能冒充原记录',async({page})=>{
 await boot(page,historyRights);let lists=0,details=0;
 await page.route(`**/api/v1/webhooks/subscriptions/${id}/deliveries?*`,route=>{lists++;return lists===1?route.abort('failed'):json(route,{items:[history],page:1,pageSize:20,total:1});});
 await page.route(`**/api/v1/webhooks/subscriptions/${id}/deliveries/${deliveryA}`,route=>{details++;return json(route,details===1?{...history,id:deliveryB}:history);});
 await page.getByTestId('webhook-history-open').click();await expect(page.getByTestId('webhook-history-problem')).toBeVisible();expect(lists).toBe(1);
 await page.getByTestId('webhook-history-refresh').click();await expect(page.getByTestId('webhook-history-detail')).toBeEnabled();expect(lists).toBe(2);
 await page.getByTestId('webhook-history-detail').click();await expect(page.getByTestId('webhook-history-detail-problem')).toBeVisible();await expect(page.getByTestId('webhook-history-detail-panel')).not.toContainText(deliveryB);
 await page.getByTestId('webhook-history-detail').click();await expect(page.getByTestId('webhook-history-detail-panel')).toContainText(deliveryA);expect(details).toBe(2);
});
test('关闭历史在途读取会中止，迟到列表不恢复原抽屉',async({page})=>{
 await boot(page,historyRights);let release,started=false,finished=false,reads=0;const pending=new Promise(resolve=>{release=resolve;});
 await page.route(`**/api/v1/webhooks/subscriptions/${id}/deliveries?*`,async route=>{reads++;if(reads===1){started=true;await pending;await json(route,{items:[history],page:1,pageSize:20,total:1}).catch(()=>{});finished=true;}else await json(route,{items:[history],page:1,pageSize:20,total:1});});
 await page.getByTestId('webhook-history-open').click();await expect.poll(()=>started).toBe(true);await page.locator('.el-drawer__close-btn').click();await expect(page.getByRole('dialog')).toHaveCount(0);release();await expect.poll(()=>finished).toBe(true);await expect(page.getByTestId('webhook-history')).toHaveCount(0);
 await page.getByTestId('webhook-history-open').click();await expect(page.getByTestId('webhook-history-detail')).toBeEnabled();expect(reads).toBe(2);
});

const attemptId='019bc2b1-2a40-7cc3-8992-a80de51bf294';
const attempt={id:attemptId,tenantId:id,subscriptionId:id,deliveryId:deliveryA,outcomeKey:'unconfirmed',startedAtUtc:'2026-11-04T00:00:00Z',completedAtUtc:null,httpStatus:null,errorCode:null,appliedToDelivery:null};
async function openAttempts(page) {
 await page.route(`**/api/v1/webhooks/subscriptions/${id}/deliveries?*`,route=>json(route,{items:[history],page:1,pageSize:20,total:1}));
 await page.route(`**/api/v1/webhooks/subscriptions/${id}/deliveries/${deliveryA}`,route=>json(route,history));
 await page.getByTestId('webhook-history-open').click();await page.getByTestId('webhook-history-detail').click();await page.getByTestId('webhook-attempts-open').click();
}
test('逐次记录明确区分未确认和旧租约应答，读取不会重发',async({page},info)=>{
 const state=await boot(page,historyRights);let reads=0;
 await page.route(`**/api/v1/webhooks/subscriptions/${id}/deliveries/${deliveryA}/attempts?*`,route=>{reads++;return json(route,{items:[attempt,{...attempt,id:second,outcomeKey:'delivered',completedAtUtc:'2026-11-04T00:01:00Z',httpStatus:200,appliedToDelivery:false}],page:1,pageSize:20,total:2});});
 await openAttempts(page);const panel=page.getByTestId('webhook-attempts');await expect(panel).toContainText('未确认');await expect(panel).toContainText('收到成功应答');await expect(panel).toContainText('当前租约已变化');expect(reads).toBe(1);
 await panel.screenshot({path:path.resolve(info.outputDir,'..','webhook-attempts.png'),animations:'disabled'});
 expect((await new AxeBuilder({page}).include('[role="dialog"]').withTags(['wcag2a','wcag2aa','wcag21a','wcag21aa']).analyze()).violations).toEqual([]);
 expect(state.creates).toBe(0);expect(state.statuses).toBe(0);
});
test('逐次读取失败可手动重读，关闭后的迟到结果不能重新打开',async({page})=>{
 await boot(page,historyRights);let reads=0,release,finished=false;const pending=new Promise(resolve=>{release=resolve;});
 await page.route(`**/api/v1/webhooks/subscriptions/${id}/deliveries/${deliveryA}/attempts?*`,async route=>{reads++;if(reads===1)return route.abort('failed');if(reads===2)return json(route,{items:[attempt],page:1,pageSize:20,total:1});await pending;await json(route,{items:[attempt],page:1,pageSize:20,total:1}).catch(()=>{});finished=true;});
 await openAttempts(page);await expect(page.getByTestId('webhook-attempts-problem')).toBeVisible();expect(reads).toBe(1);
 await page.getByTestId('webhook-attempts-refresh').click();await expect(page.getByTestId('webhook-attempts')).toContainText('未确认');expect(reads).toBe(2);
 await page.getByTestId('webhook-attempts-refresh').click();await expect.poll(()=>reads).toBe(3);await page.getByTestId('webhook-attempts-close').click();await expect(page.getByTestId('webhook-attempts')).toHaveCount(0);
 release();await expect.poll(()=>finished).toBe(true);await expect(page.getByTestId('webhook-attempts')).toHaveCount(0);
});
