import {test,expect} from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import {readFileSync,writeFileSync,existsSync} from 'node:fs';
import path from 'node:path';
const ready=process.env.FULLNET_K05_BROWSER_READY;const fixture=JSON.parse(readFileSync(ready,'utf8'));const bridge=path.dirname(ready);
if(!fixture.username||!fixture.knowledgeBaseId)throw new Error('Missing exact K05 Native fixture');
process.env.FULLNET_E2E_USERNAME=fixture.username;process.env.FULLNET_E2E_PASSWORD=fixture.password;
const {loginAsHostAdmin}=await import('./support/real-stack-auth.mjs');
for(const viewport of [{width:1440,height:1000},{width:390,height:844}])test(`维护范围任务与发布待办 ${viewport.width}`,async({page},testInfo)=>{
  test.setTimeout(180_000);const errors=[];let writes=0;page.on('pageerror',error=>errors.push(error.message));page.on('request',request=>{if(request.method()==='POST'&&/\/(index|ask|compare|refine|publication-requests|rebuild|retry)$/.test(request.url()))writes++;});
  await page.setViewportSize({width:1440,height:1000});await loginAsHostAdmin(page);await page.setViewportSize(viewport);await page.goto('/#/ai/knowledge-operations');
  const view=page.getByTestId('knowledge-operations');await expect(view).toBeVisible();
  async function refresh(key='Enter'){const next=page.waitForResponse(response=>response.url().includes('/api/v1/ai/knowledge-operations')&&response.request().method()==='GET');await view.getByTestId('operations-refresh').focus();await page.keyboard.press(key);const response=await next;expect(response.status()).toBe(200);const data=await response.json();expect(JSON.stringify(data)).not.toContain('PRIVATE_');return data;}
  const first=await refresh();expect(first.items).toHaveLength(25);await expect(view.getByTestId('operations-row')).toHaveCount(25);
  await expect(view).toContainText('不推算');await expect(view).toContainText('处理完成');
  let next=page.waitForResponse(response=>response.url().includes('/api/v1/ai/knowledge-operations')&&response.request().method()==='GET');
  await view.getByTestId('operations-next').focus();await page.keyboard.press('Enter');const second=await (await next).json();expect(second.fromUtc).toBe(first.fromUtc);expect(second.toUtc).toBe(first.toUtc);expect(second.items).toHaveLength(4);
  expect(second.items.some(row=>row.sourceKind==='faq-revision-v1')).toBe(true);await expect(view).toContainText('FAQ 静态修订');
  async function filter(name){await view.getByTestId('operations-view').click();await page.getByRole('option',{name,exact:true}).click();await expect(view.getByTestId('operations-row')).toHaveCount(0);}
  await filter('失败／租约待办');const failures=await refresh('Space');expect(failures.items).toHaveLength(2);await expect(view).toContainText('任务租约已到期');await expect(view).toContainText('ai.knowledge.index_unavailable');
  await filter('过期发布待办');const expired=await refresh();expect(expired.items).toHaveLength(1);await expect(view.getByTestId('operations-publication-state')).toContainText('已过期');await expect(view.getByTestId('operations-task-state')).toContainText('处理完成');
  await page.evaluate(async()=>{await Promise.all(document.getAnimations().filter(a=>Number.isFinite(a.effect?.getComputedTiming().endTime)).map(a=>a.finished.catch(()=>{})));});
  expect((await new AxeBuilder({page}).include('[data-testid="knowledge-operations"]').analyze()).violations).toEqual([]);
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=document.documentElement.clientWidth+1)).toBe(true);await page.keyboard.press('Tab');expect(await page.evaluate(()=>!!document.activeElement?.closest('[data-testid="knowledge-operations"]'))).toBe(true);
  await page.screenshot({path:testInfo.outputPath('operations-expired.png')});
  if(viewport.width===390){writeFileSync(path.join(bridge,'advance.json'),'{}');await expect.poll(()=>existsSync(path.join(bridge,'changed.json')),{timeout:60_000}).toBe(true);const empty=await refresh();expect(empty.items).toHaveLength(0);await expect(view.getByTestId('operations-row')).toHaveCount(0);await expect(view).toContainText('当前窗口没有管理范围内的记录');await page.screenshot({path:testInfo.outputPath('operations-cleared.png')});expect(writes).toBe(0);expect(errors).toEqual([]);writeFileSync(path.join(bridge,'complete.json'),'{}');}
  expect(writes).toBe(0);expect(errors).toEqual([]);
});
