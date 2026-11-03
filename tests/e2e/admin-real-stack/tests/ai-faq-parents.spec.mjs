import {test,expect} from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import {readFileSync,writeFileSync,existsSync} from 'node:fs';
import path from 'node:path';
const ready=process.env.FULLNET_K04_PARENTS_BROWSER_READY;const fixture=JSON.parse(readFileSync(ready,'utf8'));const bridge=path.dirname(ready);
// 桥接文件由默认 C# 序列化器写入，嵌套 DTO 字段使用 PascalCase。
const source={documentId:fixture.source.DocumentId,faqId:fixture.source.FaqId};if(!source.documentId||!source.faqId)throw new Error('Missing exact FAQ fixture identity');
process.env.FULLNET_E2E_USERNAME=fixture.username;process.env.FULLNET_E2E_PASSWORD=fixture.password;
const {loginAsHostAdmin}=await import('./support/real-stack-auth.mjs');
for(const viewport of [{width:1440,height:1000},{width:390,height:844}])test(`真实父 FAQ 搜索派生及撤销后清空 ${viewport.width}`,async({page},testInfo)=>{
  test.setTimeout(300_000);const errors=[];let paid=0;page.on('pageerror',e=>errors.push(e.message));page.on('request',r=>{if(r.method()==='POST'&&/\/(ask|compare|refine)$/.test(r.url()))paid++;});
  await page.setViewportSize({width:1440,height:1000});await loginAsHostAdmin(page);await page.setViewportSize(viewport);await page.goto('/#/ai/knowledge-search');
  const view=page.getByTestId('knowledge-search');await view.getByTestId('knowledge-base-select').click();await page.getByRole('option',{name:fixture.knowledgeName,exact:true}).click();
  await view.getByTestId('search-query').fill('标准');await expect(view.getByTestId('search-submit')).toBeEnabled();
  let response=page.waitForResponse(r=>r.url().endsWith('/knowledge-search')&&r.request().method()==='POST');await view.getByTestId('search-submit').focus();await page.keyboard.press('Enter');expect((await response).status()).toBe(200);
  // 同一 FAQ 的问题和答案可分别命中；选择首个准确标题，随后核对冻结来源唯一身份。
  const hit=view.getByTestId('search-hit').filter({has:page.getByRole('heading',{name:'三级派生标准 FAQ',exact:true})}).first();await expect(hit).toBeVisible();
  response=page.waitForResponse(r=>r.url().endsWith('/knowledge-search')&&r.request().method()==='POST');await hit.getByTestId('search-only-faq').click();
  const selected=await (await response).json();expect(selected.sources).toHaveLength(1);expect(selected.sources[0].documentId).toBe(source.documentId);
  await view.getByTestId('search-faq').click();const dialog=page.getByTestId('faq-dialog');await expect(dialog.getByTestId('faq-basis')).toHaveCount(0);
  await dialog.getByTestId('knowledge-base-select').click();await page.getByRole('option',{name:fixture.targetName,exact:true}).click();
  await dialog.getByTestId('faq-question').fill(`真实四级派生 ${viewport.width}`);await dialog.getByTestId('faq-answer').fill('人工核对的派生答案，保留全部父 FAQ 资格。');await dialog.getByTestId('faq-category').fill('制度');
  response=page.waitForResponse(r=>r.url().endsWith('/faq-drafts/from-sources')&&r.request().method()==='POST',{timeout:120_000});
  await dialog.getByTestId('faq-save').focus();await page.keyboard.press('Enter');const saved=await response;expect(saved.status()).toBe(200);const mutation=await saved.json();
  expect(mutation.workspace.origin).toBe('faq_derived');expect(mutation.workspace.graph.nodes).toHaveLength(3);expect(mutation.workspace.graph.roots).toEqual([source.documentId]);
  await expect(dialog.getByTestId('faq-parent-graph')).toContainText(source.faqId);await expect(dialog.getByTestId('faq-saved')).toBeVisible();
  expect((await new AxeBuilder({page}).include('[data-testid="faq-dialog"]').analyze()).violations).toEqual([]);
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=document.documentElement.clientWidth+1)).toBe(true);await page.keyboard.press('Tab');expect(await page.evaluate(()=>!!document.activeElement?.closest('[data-testid="faq-dialog"]'))).toBe(true);
  await page.screenshot({path:testInfo.outputPath('parent-faq-draft.png')});
  await dialog.getByTestId('faq-source').filter({hasText:source.documentId}).getByTestId('faq-preview').click();const preview=page.getByTestId('knowledge-source-dialog');
  await expect(preview.getByTestId('knowledge-source-download')).toHaveCount(0);await expect(preview.getByTestId('knowledge-source-read')).toHaveCount(0);
  response=page.waitForResponse(r=>r.url().endsWith('/faqs/'+source.faqId)&&r.request().method()==='GET',{timeout:120_000});await preview.getByTestId('knowledge-faq-source-read').click();expect((await response).status()).toBe(200);
  await expect(preview.getByTestId('faq-published-parent-graph')).toBeVisible();expect((await new AxeBuilder({page}).include('[data-testid="knowledge-source-dialog"]').analyze()).violations).toEqual([]);
  await page.screenshot({path:testInfo.outputPath('parent-faq-source.png')});
  if(viewport.width===390){
    writeFileSync(path.join(bridge,'advance.json'),JSON.stringify({advance:true}));await expect.poll(()=>existsSync(path.join(bridge,'revoked.json')),{timeout:150_000}).toBe(true);
    response=page.waitForResponse(r=>r.url().endsWith('/faqs/'+source.faqId)&&r.request().method()==='GET',{timeout:120_000});await preview.getByTestId('knowledge-faq-source-read').click();expect((await response).status()).toBe(409);
    await expect(page.getByTestId('knowledge-faq-source-body')).toHaveCount(0);await expect(page.getByTestId('faq-answer')).toHaveCount(0);await expect(page.getByTestId('faq-parent-graph')).toHaveCount(0);
    writeFileSync(path.join(bridge,'complete.json'),JSON.stringify({passed:true}));
  }
  expect(paid).toBe(0);expect(errors).toEqual([]);
});
