import {test,expect} from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import {readFileSync,writeFileSync,existsSync} from 'node:fs';
import path from 'node:path';
const ready=process.env.FULLNET_K04_DRAFT_BROWSER_READY;
const fixture=JSON.parse(readFileSync(ready,'utf8'));const bridge=path.dirname(ready);
process.env.FULLNET_E2E_USERNAME=fixture.username;process.env.FULLNET_E2E_PASSWORD=fixture.password;
const {loginAsHostAdmin}=await import('./support/real-stack-auth.mjs');
for(const viewport of [{width:1440,height:1000},{width:390,height:844}]){
  test(`不可变 FAQ 草稿维护及来源替换 ${viewport.width}`,async({page},testInfo)=>{
    test.setTimeout(300_000);const errors=[];let paid=0;page.on('pageerror',e=>errors.push(e.message));page.on('request',r=>{if(r.method()==='POST'&&/\/(ask|compare|refine)$/.test(r.url()))++paid;});
    await page.setViewportSize({width:1440,height:1000});await loginAsHostAdmin(page);await page.setViewportSize(viewport);
    await page.goto('/#/ai/knowledge-faqs');const view=page.getByTestId('faq-workbench');
    await view.getByTestId('knowledge-base-select').click();await page.getByRole('option',{name:fixture.knowledgeName,exact:true}).click();
    await view.getByTestId('faq-create').click();let dialog=page.getByTestId('faq-dialog');
    await dialog.getByTestId('faq-question').fill(`独立问题 ${viewport.width}`);await dialog.getByTestId('faq-answer').fill('独立核对答案');await dialog.getByTestId('faq-category').fill('制度');await dialog.getByTestId('faq-basis').fill('人工编写依据');
    let response=page.waitForResponse(r=>r.url().endsWith('/faq-drafts/independent')&&r.request().method()==='POST',{timeout:90_000});
    await dialog.getByTestId('faq-save').focus();await page.keyboard.press('Enter');expect((await response).status()).toBe(200);await expect(dialog.getByTestId('faq-saved')).toBeVisible();
    await dialog.getByTestId('faq-answer').fill('第二次修订答案');response=page.waitForResponse(r=>r.url().includes('/faq-drafts/')&&r.request().method()==='PUT',{timeout:90_000});await dialog.getByTestId('faq-save').click();expect((await response).status()).toBe(200);
    await dialog.getByTestId('faq-history').click();await expect(dialog.getByTestId('faq-revision')).toHaveCount(2);
    response=page.waitForResponse(r=>r.url().endsWith('/restore')&&r.request().method()==='POST',{timeout:90_000});await dialog.getByTestId('faq-restore').last().click();expect((await response).status()).toBe(200);await expect(dialog.getByTestId('faq-answer')).toHaveValue('独立核对答案');
    response=page.waitForResponse(r=>r.url().endsWith('/copy')&&r.request().method()==='POST',{timeout:90_000});await dialog.getByTestId('faq-copy').click();expect((await response).status()).toBe(200);await expect(dialog.getByTestId('faq-state')).toContainText('独立编写');
    await dialog.getByRole('button',{name:'关闭',exact:true}).click();
    await page.goto('/#/ai/knowledge-search');const search=page.getByTestId('knowledge-search');await search.getByTestId('knowledge-base-select').click();await page.getByRole('option',{name:fixture.sourceKnowledgeName,exact:true}).click();
    await search.getByTestId('search-query').fill('明确 补写');await search.getByTestId('search-submit').click();await expect(search.getByTestId('search-hit').first()).toBeVisible();await search.getByTestId('search-faq').click();dialog=page.getByTestId('faq-dialog');
    await dialog.getByTestId('knowledge-base-select').click();await dialog.getByRole('option',{name:fixture.knowledgeName,exact:true}).click();await expect(dialog.getByTestId('faq-basis')).toHaveCount(0);
    const question=`派生问题 ${viewport.width}`;await dialog.getByTestId('faq-question').fill(question);await dialog.getByTestId('faq-answer').fill('由全部当前发布文档人工核对');await dialog.getByTestId('faq-category').fill('制度');
    response=page.waitForResponse(r=>r.url().endsWith('/faq-drafts/from-documents')&&r.request().method()==='POST',{timeout:90_000});await dialog.getByTestId('faq-save').click();const saved=await response;expect(saved.status()).toBe(200);const mutation=await saved.json();
    await expect(dialog.getByTestId('faq-state')).toContainText('文档派生');await expect(dialog.getByTestId('faq-source')).toHaveCount(1);expect(mutation.workspace.origin).toBe('document_derived');
    await dialog.getByTestId('faq-preview').click();const preview=page.getByTestId('knowledge-source-dialog');await preview.getByTestId('knowledge-source-read').click();await expect(preview).toContainText('原文件😀识别内容。');await preview.getByRole('button',{name:'取消',exact:true}).click();await expect(preview).toBeHidden();
    expect((await new AxeBuilder({page}).include('.faq-draft-dialog').withTags(['wcag2a','wcag2aa']).analyze()).violations).toEqual([]);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=document.documentElement.clientWidth+1)).toBe(true);await page.screenshot({path:testInfo.outputPath('faq-draft.png')});
    if(viewport.width===1440){writeFileSync(path.join(bridge,'advance.json'),JSON.stringify({advance:true}));await expect.poll(()=>existsSync(path.join(bridge,'replaced.json')),{timeout:150_000}).toBe(true);
      response=page.waitForResponse(r=>r.url().endsWith('/faq-drafts/'+mutation.workspace.id)&&r.request().method()==='GET',{timeout:90_000});await dialog.getByTestId('faq-reload').click();expect((await response).status()).toBe(409);await expect(search.getByTestId('search-hit')).toHaveCount(0);await expect(page.getByTestId('faq-dialog')).toHaveCount(0);
    }
    expect(paid).toBe(0);expect(errors).toEqual([]);
  });
}
