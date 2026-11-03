import {test,expect} from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import {readFileSync,writeFileSync,renameSync} from 'node:fs';
import path from 'node:path';
import {loginAsHostUser} from './support/real-stack-auth.mjs';
const ready=process.env.FULLNET_K06_FAQ_BROWSER_READY;if(!ready)throw Error('Missing new business FAQ bridge');
const fixture=JSON.parse(readFileSync(ready,'utf8'));const bridge=path.dirname(ready);
function marker(name,value){const file=path.join(bridge,name+'.writing');writeFileSync(file,JSON.stringify(value));renameSync(file,path.join(bridge,name+'.json'));}
test.afterEach(async({},info)=>{if(info.status!==info.expectedStatus)marker('failed',{passed:false});});
for(const [index,viewport] of [{width:1440,height:1000},{width:390,height:844}].entries())test(`本人日程FAQ完整来源与撤回 ${viewport.width}`,async({page},info)=>{
  test.setTimeout(240_000);page.setDefaultTimeout(15_000);const user=fixture.users[index];const errors=[];let paid=0;
  page.on('pageerror',error=>errors.push(error.message));page.on('request',request=>{if(request.method()==='POST'&&/\/(ask|compare|refine|index)$/.test(request.url()))paid++;});
  await page.setViewportSize({width:1440,height:1000});await loginAsHostUser(page,user.username,fixture.password);await page.setViewportSize(viewport);
  await page.goto('/#/ai/knowledge-search');const search=page.getByTestId('knowledge-search');await search.getByTestId('knowledge-base-select').click();
  await page.getByRole('option',{name:fixture.sourceKnowledgeName,exact:true}).click();await search.getByTestId('search-query').fill('人工');
  // 先等待真实搜索响应，避免默认五秒DOM断言提前终止另一视口的同库验收。
  await expect(search.getByTestId('search-submit')).toBeEnabled({timeout:30_000});
  const searched=page.waitForResponse(response=>response.request().method()==='POST'&&response.url().endsWith('/api/v1/ai/knowledge-search'),{timeout:90_000});
  await search.getByTestId('search-submit').focus();await page.keyboard.press('Enter');expect((await searched).status()).toBe(200);
  await expect(search.getByTestId('search-faq')).toBeVisible({timeout:30_000});await search.getByTestId('search-faq').click();const dialog=page.getByTestId('faq-dialog');
  await dialog.getByTestId('knowledge-base-select').click();await dialog.getByRole('option',{name:user.knowledgeName,exact:true}).click();
  await dialog.getByTestId('faq-schedule-id').fill(user.scheduleId);await expect(dialog.getByTestId('faq-capture-schedule')).toBeEnabled();await dialog.getByTestId('faq-capture-schedule').focus();await page.keyboard.press('Space');
  await expect(dialog.getByTestId('faq-business-sources')).toContainText(user.scheduleId);await dialog.getByTestId('faq-question').fill('本人日程标准问题 '+viewport.width);
  await dialog.getByTestId('faq-answer').fill('根据本人日程和文档人工核对，不自动发布。');await dialog.getByTestId('faq-category').fill('私人制度');
  const saved=page.waitForResponse(response=>response.request().method()==='POST'&&response.url().endsWith('/faq-drafts/from-documents'),{timeout:90_000});
  await expect(dialog.getByTestId('faq-save')).toBeEnabled();await dialog.getByTestId('faq-save').focus();await page.keyboard.press('Enter');const response=await saved;expect(response.status()).toBe(200);const mutation=await response.json();
  expect(mutation.workspace.personalSchedules).toHaveLength(1);expect(mutation.workspace.personalSchedules[0].id).toBe(user.scheduleId);await expect(dialog.getByTestId('faq-saved')).toBeVisible();
  await dialog.getByRole('button',{name:'预览本人日程',exact:true}).focus();await page.keyboard.press('Enter');const source=page.getByTestId('personal-schedule-source-dialog');
  await expect(source.getByTestId('personal-schedule-source')).toContainText(user.content);await expect(source.getByTestId('schedule-ask-panel')).toHaveCount(0);
  await page.evaluate(async()=>{await Promise.all(document.getAnimations().filter(value=>Number.isFinite(value.effect?.getComputedTiming().endTime)).map(value=>value.finished.catch(()=>{})));});
  expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=document.documentElement.clientWidth+1)).toBe(true);
  await page.screenshot({path:info.outputPath('faq-business-source.png')});
  await page.evaluate(async user=>{const api=await import('/src/api/personal-schedules.ts');await api.updatePersonalSchedule(user.scheduleId,{content:'已经改变的FAQ来源',startAtUtc:user.startAtUtc,endAtUtc:user.endAtUtc,version:user.version});},user);
  await source.getByRole('button',{name:'重新读取',exact:true}).focus();await page.keyboard.press('Enter');await expect(page.getByTestId('faq-dialog')).toHaveCount(0);await expect(page.getByTestId('personal-schedule-source')).toHaveCount(0);await expect(search.getByTestId('search-hit')).toHaveCount(0);
  const [denied]=await Promise.all([page.waitForResponse(value=>value.request().method()==='GET'&&value.url().endsWith('/faq-drafts/'+mutation.workspace.id),{timeout:30_000}),
    page.evaluate(async value=>{const api=await import('/src/api/ai-knowledge-faqs.ts');try{await api.getFaqDraft({knowledgeBaseId:value.knowledgeBaseId,faqId:value.id});}catch{}},mutation.workspace)]);
  expect(denied.status()).toBe(409);expect(paid).toBe(0);expect(errors).toEqual([]);expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);
  await page.screenshot({path:info.outputPath('faq-business-invalidated.png')});marker('complete-'+viewport.width,{passed:true});
});
