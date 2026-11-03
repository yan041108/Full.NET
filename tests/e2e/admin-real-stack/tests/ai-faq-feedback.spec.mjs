import {test,expect} from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import {readFileSync,writeFileSync,existsSync} from 'node:fs';
import path from 'node:path';
const ready=process.env.FULLNET_K04_FEEDBACK_BROWSER_READY;const fixture=JSON.parse(readFileSync(ready,'utf8'));const bridge=path.dirname(ready);
if(!fixture.feedbackId||!fixture.feedbackVersion||!fixture.reviewVersion||!fixture.targetId)throw new Error('Missing exact approved feedback identity');
process.env.FULLNET_E2E_USERNAME=fixture.username;process.env.FULLNET_E2E_PASSWORD=fixture.password;
const {loginAsHostAdmin}=await import('./support/real-stack-auth.mjs');
for(const viewport of [{width:1440,height:1000},{width:390,height:844}])test(`真实已批准私人反馈转草稿及撤回清空 ${viewport.width}`,async({page},testInfo)=>{
  test.setTimeout(300_000);const errors=[];let paid=0,previews=0;page.on('pageerror',e=>errors.push(e.message));page.on('request',request=>{
    if(request.method()==='POST'&&/\/(ask|compare|refine|index)$/.test(request.url()))paid++;
    if(request.method()==='POST'&&request.url().endsWith('/faq-drafts/feedback-preview'))previews++;
  });
  await page.setViewportSize({width:1440,height:1000});await loginAsHostAdmin(page);await page.setViewportSize(viewport);await page.goto('/#/ai/chat');
  await page.getByTestId('feedback-review-open').click();
  const open=page.locator(`[data-testid="faq-feedback-open"][data-feedback-id="${fixture.feedbackId}"]`);
  await expect(open).toBeEnabled({timeout:120_000});await open.focus();await page.keyboard.press('Enter');
  const dialog=page.getByTestId('faq-feedback-dialog');await expect(dialog).toBeVisible();await expect(dialog.getByTestId('faq-feedback-answer')).toHaveCount(0);expect(previews).toBe(0);
  const select=dialog.getByTestId('knowledge-base-select');await select.click();await page.getByRole('option',{name:fixture.lowTargetName,exact:true}).click();
  await dialog.getByTestId('faq-feedback-preview').focus();await page.keyboard.press('Space');await expect(dialog.getByTestId('faq-feedback-restricted')).toBeVisible();expect(previews).toBe(0);
  await select.click();await page.getByRole('option',{name:fixture.targetName,exact:true}).click();
  let response=page.waitForResponse(r=>r.url().endsWith('/faq-drafts/feedback-preview')&&r.request().method()==='POST',{timeout:120_000});
  await dialog.getByTestId('faq-feedback-preview').focus();await page.keyboard.press('Enter');const previewResponse=await response;expect(previewResponse.status()).toBe(200);const preview=await previewResponse.json();
  expect(preview.feedback.feedbackId).toBe(fixture.feedbackId);expect(preview.feedback.feedbackVersion).toBe(fixture.feedbackVersion);expect(preview.feedback.reviewVersion).toBe(fixture.reviewVersion);
  expect(JSON.stringify(preview)).not.toContain('PRIVATE_USER_QUESTION');expect(preview.sources).toHaveLength(2);expect(preview.graph.nodes).toHaveLength(1);
  await expect(dialog.getByTestId('faq-feedback-answer')).toHaveAttribute('readonly','');
  await expect(dialog.getByTestId('faq-feedback-answer')).toHaveValue(preview.answer);
  await dialog.getByTestId('faq-feedback-question').fill(`私人反馈人工标准问题 ${viewport.width}`);
  await dialog.getByTestId('faq-feedback-category').fill('私人来源');
  response=page.waitForResponse(r=>r.url().endsWith('/faq-drafts/from-feedback')&&r.request().method()==='POST',{timeout:120_000});
  await dialog.getByTestId('faq-feedback-save').focus();await page.keyboard.press('Enter');const savedResponse=await response;expect(savedResponse.status()).toBe(200);
  const submitted=savedResponse.request().postDataJSON();expect(submitted.content).not.toHaveProperty('answer');expect(submitted).not.toHaveProperty('sources');
  const mutation=await savedResponse.json();expect(mutation.workspace.origin).toBe('feedback_derived');expect(mutation.workspace.sourceHash).toBe(preview.sourceHash);expect(mutation.workspace.feedback).toEqual(preview.feedback);
  expect(mutation.workspace.revision.content.answer).toBe(preview.answer);expect(mutation.workspace.headVersion).toBe(1);
  await expect(dialog.getByTestId('faq-feedback-saved')).toBeVisible();await expect(dialog.getByTestId('faq-feedback-graph')).toBeVisible();
  await page.evaluate(async()=>{await Promise.all(document.getAnimations().filter(a=>Number.isFinite(a.effect?.getComputedTiming().endTime)).map(a=>a.finished.catch(()=>{})));});
  expect((await new AxeBuilder({page}).include('[data-testid="faq-feedback-dialog"]').analyze()).violations).toEqual([]);
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=document.documentElement.clientWidth+1)).toBe(true);
  await page.keyboard.press('Tab');expect(await page.evaluate(()=>!!document.activeElement?.closest('[data-testid="faq-feedback-dialog"]'))).toBe(true);
  await page.screenshot({path:testInfo.outputPath('private-feedback-draft.png')});
  await dialog.getByTestId('faq-feedback-edit').click();const editor=page.getByTestId('faq-dialog');await expect(editor).toBeVisible();
  await expect(editor.getByTestId('faq-origin-feedback')).toContainText(fixture.feedbackId);
  await page.evaluate(async()=>{await Promise.all(document.getAnimations().filter(a=>Number.isFinite(a.effect?.getComputedTiming().endTime)).map(a=>a.finished.catch(()=>{})));});
  expect((await new AxeBuilder({page}).include('[data-testid="faq-dialog"]').analyze()).violations).toEqual([]);
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=document.documentElement.clientWidth+1)).toBe(true);
  await page.screenshot({path:testInfo.outputPath('private-feedback-editor.png')});
  if(viewport.width===390){
    writeFileSync(path.join(bridge,'advance.json'),JSON.stringify({advance:true}));await expect.poll(()=>existsSync(path.join(bridge,'revoked.json')),{timeout:150_000}).toBe(true);
    // 重新读取父弹窗的明确预览入口，失效后父、子编辑器不得保留私人答案。
    await editor.getByRole('button',{name:'关闭',exact:true}).click();
    response=page.waitForResponse(r=>r.url().endsWith('/faq-drafts/feedback-preview')&&r.request().method()==='POST',{timeout:120_000});
    await dialog.getByTestId('faq-feedback-preview').click();expect((await response).status()).toBe(409);
    await expect(page.getByTestId('faq-feedback-answer')).toHaveCount(0);await expect(page.getByTestId('faq-answer')).toHaveCount(0);
    await expect(page.getByTestId('faq-feedback-graph')).toHaveCount(0);await expect(page.getByTestId('faq-feedback-edit')).toHaveCount(0);
    writeFileSync(path.join(bridge,'complete.json'),JSON.stringify({passed:true}));
  }
  expect(paid).toBe(0);expect(errors).toEqual([]);
});
