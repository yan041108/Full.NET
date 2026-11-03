import {test,expect} from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import {readFileSync,writeFileSync,renameSync} from 'node:fs';
import path from 'node:path';
import {loginAsHostUser} from './support/real-stack-auth.mjs';
const ready=process.env.FULLNET_K06_COMPLETION_BROWSER_READY;if(!ready)throw Error('Missing new completion bridge');
const fixture=JSON.parse(readFileSync(ready,'utf8'));const bridge=path.dirname(ready);
function marker(name,value){const file=path.join(bridge,name+'.writing');writeFileSync(file,JSON.stringify(value));renameSync(file,path.join(bridge,name+'.json'));}
test.afterEach(async({},info)=>{if(info.status!==info.expectedStatus)marker('failed',{passed:false});});
for(const [index,viewport] of [{width:1440,height:1000},{width:390,height:844}].entries())test(`独立审批与准确日程完成回执 ${viewport.width}`,async({page,browser},info)=>{
  test.setTimeout(240_000);page.setDefaultTimeout(15_000);const user=fixture.users[index];const errors=[];let paid=0,created=0;
  page.on('pageerror',error=>errors.push(error.message));page.on('request',request=>{if(request.method()==='POST'&&/\/(ask|responses|embeddings)$/.test(request.url()))paid++;if(request.method()==='POST'&&request.url().endsWith('/api/v1/ai/agent/runs'))created++;});
  await page.setViewportSize({width:1440,height:1000});await loginAsHostUser(page,user.username,fixture.password);await page.setViewportSize(viewport);
  await page.goto('/#/calendar/personal-schedules');const open=page.getByTestId('personal-schedules-assistant-complete');await expect(open).toBeVisible();await open.focus();await page.keyboard.press('Enter');
  const dialog=page.getByRole('dialog',{name:'独立审批后完成日程',exact:true});await expect(dialog).toContainText(user.scheduleId);
  await dialog.getByTestId('schedule-completion-model').click();await page.getByRole('option',{name:fixture.modelName,exact:true}).click();
  const createdResponse=page.waitForResponse(r=>r.request().method()==='POST'&&r.url().endsWith('/api/v1/ai/agent/runs'));await expect(dialog.getByTestId('schedule-completion-create')).toBeEnabled();await dialog.getByTestId('schedule-completion-create').focus();await page.keyboard.press('Space');expect((await createdResponse).status()).toBe(202);
  await expect.poll(async()=>{await dialog.getByTestId('schedule-completion-refresh').click();return dialog.getByTestId('schedule-completion-run-state').textContent();},{timeout:45_000}).toContain('等待独立审批');
  await expect(dialog.getByTestId('schedule-completion-request')).toBeEnabled();const proposed=page.waitForResponse(r=>r.url().endsWith('/schedule-completion/proposal'));await dialog.getByTestId('schedule-completion-request').focus();await page.keyboard.press('Enter');expect((await proposed).status()).toBe(200);
  const confirm=page.locator('.el-message-box');await expect(confirm).toBeVisible();const requested=page.waitForResponse(r=>r.request().method()==='POST'&&r.url().endsWith('/api/v1/ai/agent/approvals'));await confirm.getByRole('button',{name:/^(确定|确认|OK)$/}).focus();await page.keyboard.press('Enter');expect((await requested).status()).toBe(200);
  const approvalInput=dialog.locator('[data-testid="schedule-completion-approval"] input, input[data-testid="schedule-completion-approval"]');await expect(approvalInput).toBeVisible();const approvalId=await approvalInput.inputValue();
  // 本人仍持有Decide，直接走正式当前会话API证明服务器拒绝自审。
  const self=await page.evaluate(async id=>{const api=await import('/src/api/ai-agent-approvals.ts');const item=await api.getAgentApproval(id);try{await api.decideAgentApproval(id,{approve:true,expectedVersion:item.version});return 200;}catch(error){return error.status;}},approvalId);expect(self).toBe(403);
  const reviewerContext=await browser.newContext({viewport:{width:1440,height:1000}});const reviewer=await reviewerContext.newPage();
  try{
    await loginAsHostUser(reviewer,user.reviewer,fixture.password);await reviewer.setViewportSize(viewport);await reviewer.goto('/#/ai/agent-runs');
    const panel=reviewer.getByTestId('ai-workflow-approval');await panel.getByRole('textbox').fill(approvalId);await expect(panel.getByTestId('ai-workflow-lookup-approval')).toBeEnabled();await panel.getByTestId('ai-workflow-lookup-approval').focus();await reviewer.keyboard.press('Enter');
    await expect(panel).toContainText(user.scheduleId);await expect(panel).toContainText(`pending -> completed; expected version: ${user.version}`);await expect(panel).toContainText('操作目标');await expect(panel).toContainText('变更内容');await expect(panel).not.toContainText(user.content);
    expect((await new AxeBuilder({page:reviewer}).analyze()).violations).toEqual([]);expect(await reviewer.evaluate(()=>document.documentElement.scrollWidth<=document.documentElement.clientWidth+1)).toBe(true);
    await reviewer.screenshot({path:info.outputPath('independent-approval.png'),fullPage:true});await expect(panel.getByTestId('ai-workflow-approve')).toBeEnabled();await panel.getByTestId('ai-workflow-approve').focus();await reviewer.keyboard.press('Space');
    await reviewer.locator('.el-message-box').getByRole('button',{name:/^(确定|确认|OK)$/}).click();await expect(panel).toContainText('已批准');
  }finally{await reviewerContext.close();}
  await dialog.getByTestId('schedule-completion-refresh').click();await expect(dialog.getByTestId('schedule-completion-resume')).toBeEnabled();await dialog.getByTestId('schedule-completion-resume').focus();await page.keyboard.press('Enter');
  await expect.poll(async()=>{await dialog.getByTestId('schedule-completion-refresh').click();return dialog.getByTestId('schedule-completion-result').textContent().catch(()=> '');},{timeout:60_000}).toContain('接收方已确认本操作完成');
  await expect(dialog).toContainText('实际完成版本');expect(created).toBe(1);expect(paid).toBe(0);expect(errors).toEqual([]);
  expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=document.documentElement.clientWidth+1)).toBe(true);
  expect(await dialog.evaluate(node=>node.scrollWidth<=node.clientWidth+1)).toBe(true);await page.screenshot({path:info.outputPath('receiver-completed.png'),fullPage:true});
  marker('complete-'+viewport.width,{passed:true});
});
