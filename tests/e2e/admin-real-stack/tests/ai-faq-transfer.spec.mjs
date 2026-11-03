import {test,expect} from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import {readFileSync,writeFileSync,existsSync} from 'node:fs';
import path from 'node:path';
const ready=process.env.FULLNET_K04_TRANSFER_BROWSER_READY;const fixture=JSON.parse(readFileSync(ready,'utf8'));const bridge=path.dirname(ready);
process.env.FULLNET_E2E_USERNAME=fixture.username;process.env.FULLNET_E2E_PASSWORD=fixture.password;
const {loginAsHostAdmin}=await import('./support/real-stack-auth.mjs');
for(const viewport of [{width:1440,height:1000},{width:390,height:844}])test(`受控 FAQ 导出预览及逐条导入 ${viewport.width}`,async({page},testInfo)=>{
  test.setTimeout(300_000);const errors=[];let paid=0;page.on('pageerror',error=>errors.push(error.message));page.on('request',request=>{if(request.method()==='POST'&&/\/(ask|compare|refine)$/.test(request.url()))paid++;});
  await page.setViewportSize({width:1440,height:1000});await loginAsHostAdmin(page);await page.setViewportSize(viewport);await page.goto('/#/ai/knowledge-faqs');const view=page.getByTestId('faq-workbench');
  await view.getByTestId('knowledge-base-select').click();await page.getByRole('option',{name:fixture.knowledgeName,exact:true}).click();await expect(view.getByTestId('faq-list-item').first()).toBeVisible();
  await view.getByTestId('faq-export').click();let dialog=page.getByTestId('faq-transfer-dialog');
  for(const name of ['传输原始独立问题','传输原始派生问题']){
    const checkbox=dialog.getByRole('checkbox',{name,exact:true}).first();await checkbox.focus();await page.keyboard.press('Space');await expect(checkbox).toBeChecked();
  }
  let response=page.waitForResponse(value=>value.url().endsWith('/faq-transfer/export')&&value.request().method()==='POST');await dialog.getByTestId('faq-transfer-export').focus();await page.keyboard.press('Enter');expect((await response).status()).toBe(200);
  const text=dialog.getByTestId('faq-transfer-json');await expect(text).not.toHaveValue('');const packageText=await text.inputValue();const pack=JSON.parse(packageText);expect(pack.items).toHaveLength(2);expect(pack.items.map(item=>item.origin).sort()).toEqual(['document_derived','independent']);expect(pack.items.find(item=>item.origin==='document_derived').sources).toHaveLength(1);
  await dialog.getByRole('button',{name:'关闭',exact:true}).click();await view.getByTestId('faq-import').click();dialog=page.getByTestId('faq-transfer-dialog');
  if(viewport.width===1440)await dialog.getByTestId('faq-transfer-file').setInputFiles({name:'faq-transfer.json',mimeType:'application/json',buffer:Buffer.from(packageText)});else await dialog.getByTestId('faq-transfer-json').fill(packageText);
  await expect(dialog.getByTestId('faq-transfer-json')).toHaveValue(packageText);
  await expect(dialog.getByTestId('faq-transfer-commit')).toBeDisabled();response=page.waitForResponse(value=>value.url().endsWith('/faq-transfer/preview')&&value.request().method()==='POST');await dialog.getByTestId('faq-transfer-preview').focus();await page.keyboard.press('Enter');expect((await response).status()).toBe(200);await expect(dialog.getByTestId('faq-transfer-report')).toContainText('文档派生');
  expect((await new AxeBuilder({page}).include('.faq-transfer-dialog').analyze()).violations).toEqual([]);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=document.documentElement.clientWidth+1)).toBe(true);await page.keyboard.press('Tab');expect(await page.evaluate(()=>!!document.activeElement?.closest('.faq-transfer-dialog'))).toBe(true);
  await page.screenshot({path:testInfo.outputPath('faq-transfer-preview.png')});
  response=page.waitForResponse(value=>value.url().endsWith('/faq-transfer/import')&&value.request().method()==='POST');await dialog.getByTestId('faq-transfer-commit').focus();await page.keyboard.press('Enter');const committed=await response;expect(committed.status()).toBe(200);const report=await committed.json();expect(report.items.every(item=>item.status==='created'&&item.faqId&&item.savedRevisionId)).toBe(true);await expect(dialog.getByTestId('faq-transfer-json')).toHaveValue('');await expect(dialog.getByTestId('faq-transfer-report')).toContainText('已创建草稿');await page.screenshot({path:testInfo.outputPath('faq-transfer-import.png')});
  if(viewport.width===1440){response=page.waitForResponse(value=>value.url().endsWith('/faq-drafts/'+report.items[1].faqId)&&value.request().method()==='GET');await dialog.getByTestId('faq-transfer-open').nth(1).click();const read=await response;expect(read.status()).toBe(200);const workspace=await read.json();expect(workspace.sourceHash).toBe(pack.items[1].sourceHash);expect(workspace.origin).toBe('document_derived');expect(workspace.sources).toEqual(pack.items[1].sources);await expect(page.getByTestId('faq-dialog').getByTestId('faq-state')).toContainText('文档派生');await page.getByTestId('faq-dialog').getByRole('button',{name:'关闭',exact:true}).click();}
  if(viewport.width===1440){
    await view.getByTestId('knowledge-base-select').click();await page.getByRole('option',{name:fixture.lowerKnowledgeName,exact:true}).click();
    await view.getByTestId('faq-import').click();dialog=page.getByTestId('faq-transfer-dialog');await dialog.getByTestId('faq-transfer-json').fill(packageText);
    response=page.waitForResponse(value=>value.url().endsWith('/faq-transfer/preview')&&value.request().method()==='POST');await dialog.getByTestId('faq-transfer-preview').click();
    const lower=await response;expect(lower.status()).toBe(200);expect((await lower.json()).items.every(item=>item.status==='classification_conflict')).toBe(true);
    await expect(dialog.getByTestId('faq-transfer-report')).toContainText('目标知识库分类低于原始来源');await expect(dialog.getByTestId('faq-transfer-json')).toHaveValue('');await expect(dialog.getByTestId('faq-transfer-commit')).toBeDisabled();
  }
  if(viewport.width===390){writeFileSync(path.join(bridge,'advance.json'),JSON.stringify({advance:true}));await expect.poll(()=>existsSync(path.join(bridge,'replaced.json')),{timeout:150_000}).toBe(true);
    await dialog.getByTestId('faq-transfer-json').fill(packageText);response=page.waitForResponse(value=>value.url().endsWith('/faq-transfer/preview')&&value.request().method()==='POST');await dialog.getByTestId('faq-transfer-preview').click();const unavailable=await response;expect(unavailable.status()).toBe(200);expect((await unavailable.json()).items.some(item=>item.status==='source_unavailable')).toBe(true);await expect(dialog.getByTestId('faq-transfer-json')).toHaveValue('');await expect(dialog.getByTestId('faq-transfer-commit')).toBeDisabled();writeFileSync(path.join(bridge,'complete.json'),JSON.stringify({passed:true}));
  }
  expect(paid).toBe(0);expect(errors).toEqual([]);
});
