import{test,expect}from'@playwright/test';
import AxeBuilder from'@axe-core/playwright';
import{readFileSync,writeFileSync,existsSync}from'node:fs';
import path from'node:path';
const fixture=JSON.parse(readFileSync(process.env.FULLNET_K04_LIFECYCLE_BROWSER_READY,'utf8'));
const bridge=path.dirname(process.env.FULLNET_K04_LIFECYCLE_BROWSER_READY);
process.env.FULLNET_E2E_USERNAME=fixture.username;process.env.FULLNET_E2E_VIEWER_USERNAME=fixture.reviewerUsername;process.env.FULLNET_E2E_PASSWORD=fixture.password;process.env.FULLNET_E2E_VIEWER_PASSWORD=fixture.password;
const{loginAsHostAdmin,loginAsHostViewer}=await import('./support/real-stack-auth.mjs');
for(const[index,viewport]of[{width:1440,height:1000},{width:390,height:844}].entries()){
  test(`FAQ 完整索引、独立审核、发布与撤销 ${viewport.width}`,async({page,browser},testInfo)=>{
    test.setTimeout(300_000);const errors=[];page.on('pageerror',e=>errors.push(e.message));await page.setViewportSize({width:1440,height:1000});await loginAsHostAdmin(page);await page.setViewportSize(viewport);
    await page.goto('/#/ai/knowledge-faqs');const view=page.getByTestId('faq-workbench');await view.getByTestId('knowledge-base-select').click();await page.getByRole('option',{name:fixture.knowledgeName,exact:true}).click();
    const row=view.getByTestId('faq-list-item').filter({hasText:index===0?'浏览器完整发布桌面':'浏览器完整发布移动'});await row.getByTestId('faq-govern').click();const dialog=page.locator('.faq-lifecycle-dialog');
    // 实际键盘确认收费，再沿焦点顺序派发，不能仅以 axe 扫描代替可操作性。
    await expect(dialog.getByTestId('faq-index')).toBeDisabled();const confirmation=dialog.getByTestId('faq-index-confirm').getByRole('checkbox');await expect(confirmation).toBeEnabled();await confirmation.focus();await confirmation.press('Space');await expect(confirmation).toBeChecked();await confirmation.press('Tab');await expect(dialog.getByTestId('faq-index')).toBeFocused();let response=page.waitForResponse(r=>r.url().endsWith('/index')&&r.request().method()==='POST');await dialog.getByTestId('faq-index').press('Enter');expect((await response).status()).toBe(202);
    await expect(dialog.getByTestId('faq-request-publication')).toBeVisible({timeout:90_000});await expect(dialog.getByTestId('faq-lifecycle-status')).toContainText('未发布');
    await dialog.getByTestId('faq-effective-from').fill(fixture.effectiveFromUtc);await dialog.getByTestId('faq-effective-until').fill(fixture.effectiveToUtc);
    response=page.waitForResponse(r=>r.url().endsWith('/publication-requests')&&r.request().method()==='POST');await dialog.getByTestId('faq-request-publication').click();const requested=await response;expect(requested.status()).toBe(201);const proposal=await requested.json();expect(proposal.faqId).toBe(fixture.faqIds[index]);
    const link=await dialog.getByTestId('faq-review-link').inputValue();expect(link).toContain('/#/ai/knowledge-faqs?');expect(link).toContain(proposal.id);
    const context=await browser.newContext({viewport});const reviewer=await context.newPage();reviewer.on('pageerror',e=>errors.push(e.message));
    try{await reviewer.setViewportSize({width:1440,height:1000});await loginAsHostViewer(reviewer);await reviewer.setViewportSize(viewport);await reviewer.goto(link);const review=reviewer.locator('.faq-lifecycle-dialog');
      await expect(review.getByTestId('faq-publication-snapshot')).toContainText('通过正式页面审核的答案。');await expect(review.getByTestId('faq-publication-snapshot')).toContainText(proposal.revisionId);
      expect((await new AxeBuilder({page:reviewer}).include('.faq-lifecycle-dialog').withTags(['wcag2a','wcag2aa']).analyze()).violations).toEqual([]);
      await expect(review.getByTestId('faq-approve')).toBeEnabled();const approved=reviewer.waitForResponse(r=>r.url().endsWith('/review')&&r.request().method()==='POST');await review.getByTestId('faq-approve').focus();await review.getByTestId('faq-approve').press('Enter');expect((await approved).status()).toBe(200);
    }finally{await context.close();}
    await dialog.getByTestId('faq-lifecycle-refresh').click();await expect(dialog.getByTestId('faq-publish')).toBeEnabled();response=page.waitForResponse(r=>r.url().endsWith('/publish')&&r.request().method()==='POST');await dialog.getByTestId('faq-publish').click();expect((await response).status()).toBe(200);
    await expect(dialog.getByTestId('faq-lifecycle-status')).toContainText('已发布');await dialog.getByTestId('faq-published-preview').click();await expect(dialog.getByTestId('faq-published-body')).toContainText('通过正式页面审核的答案。');
    expect((await new AxeBuilder({page}).include('.faq-lifecycle-dialog').withTags(['wcag2a','wcag2aa']).analyze()).violations).toEqual([]);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=document.documentElement.clientWidth+1)).toBe(true);await page.screenshot({path:testInfo.outputPath('faq-publication.png')});
    if(index===1){writeFileSync(path.join(bridge,'advance.json'),JSON.stringify({advance:true}));await expect.poll(()=>existsSync(path.join(bridge,'replaced.json')),{timeout:90_000}).toBe(true);
      response=page.waitForResponse(r=>r.url().includes('/faqs/'+fixture.faqIds[index])&&r.request().method()==='GET');await dialog.getByTestId('faq-published-preview').click();expect((await response).status()).toBe(409);await expect(dialog.getByTestId('faq-published-body')).toHaveCount(0);await expect(dialog.getByTestId('faq-publication-snapshot')).toHaveCount(0);
      response=page.waitForResponse(r=>r.url().endsWith('/publications/withdraw')&&r.request().method()==='POST');await dialog.getByTestId('faq-withdraw-unavailable').click();expect((await response).status()).toBe(200);
    }else{response=page.waitForResponse(r=>r.url().endsWith('/publications/withdraw')&&r.request().method()==='POST');await dialog.getByTestId('faq-withdraw').click();expect((await response).status()).toBe(200);await expect(dialog.getByTestId('faq-published-body')).toHaveCount(0);await dialog.getByTestId('faq-lifecycle-refresh').click();await expect(dialog.getByTestId('faq-lifecycle-status')).toContainText('未发布');}
    expect(errors).toEqual([]);
  });
}
