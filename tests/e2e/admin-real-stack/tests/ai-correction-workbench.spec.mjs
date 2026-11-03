import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { readFileSync } from 'node:fs';

// 只消费本批 Linux 夹具给出的隔离用户和对象；不使用 route mock 或 Windows .NET 后端。
const fixture = JSON.parse(readFileSync(process.env.FULLNET_K02_BROWSER_READY, 'utf8'));
process.env.FULLNET_E2E_USERNAME = fixture.username;
process.env.FULLNET_E2E_PASSWORD = fixture.password;
const { loginAsHostAdmin, trackUiAccessToken, crudTableRow } = await import('./support/real-stack-auth.mjs');
const api = process.env.FULLNET_E2E_API_URL;
const origin = process.env.FULLNET_E2E_ADMIN_ORIGIN ?? 'http://localhost:25173';

for (const viewport of [{ width: 1440, height: 1000 }, { width: 390, height: 844 }]) {
  test(`真实 Native/Worker 校对、冲突保稿与恢复 ${viewport.width}`, async ({ page, request }, testInfo) => {
    test.setTimeout(150_000);
    const errors = []; page.on('pageerror', e => errors.push(e.message));
    const currentToken = trackUiAccessToken(page);
    // 通用登录助手会展开桌面侧栏；校对页面本身再按目标视口验收，避免把折叠侧栏当作业务失败。
    await page.setViewportSize({ width: 1440, height: 1000 }); await loginAsHostAdmin(page);
    await page.setViewportSize(viewport);
    // 单客户端登录策略会撤销旧会话；API 断言沿用当前 UI 身份，不二次登录使页面失效。
    const headers = { Authorization: `Bearer ${currentToken()}`, Origin: origin };
    // 每次实际运行创建独立来源，失败重试不继承前轮已提交或已删除的校对头。
    const collection = `/api/v1/ai/knowledge-bases/${fixture.knowledgeBaseId}/documents`;
    const title = `真实校对 ${viewport.width} ${crypto.randomUUID().slice(0, 8)}`;
    const created = await request.post(api + collection, { headers, data: { title, description: null } });
    expect(created.status()).toBe(201); const document = await created.json();
    const path = collection + '/' + document.id;
    const uploaded = await request.post(api + path + `/versions?version=${document.version}&requestKey=${crypto.randomUUID()}&fileName=source.txt`, {
      headers: { ...headers, 'Content-Type': 'text/plain; charset=utf-8' }, data: Buffer.from(fixture.original, 'utf8') });
    expect(uploaded.status()).toBe(202); const { versionId } = await uploaded.json();
    const root = path + `/versions/${versionId}/corrections`;
    await page.goto('/#/ai/knowledge-documents');
    await page.getByTestId('knowledge-base-select').click(); await page.getByRole('option', { name: fixture.knowledgeName, exact: true }).click();
    const row = crudTableRow(page, 'vue-admin', title);
    await row.getByTestId('knowledge-versions').click(); await page.getByTestId('knowledge-correction').click();
    const dialog = page.getByTestId('knowledge-correction-dialog');
    await dialog.getByTestId('correction-prepare').click();
    await expect(dialog.getByTestId('correction-text')).toHaveText(fixture.original, { timeout: 45_000 });
    // 新来源预览与当前人工头有独立身份；采用当前头才能进行人工保存。
    await dialog.getByTestId('correction-current').click();
    await dialog.getByTestId('correction-edit-part').click();
    await dialog.getByTestId('correction-draft-text').fill('真实浏览器更正');
    await dialog.getByTestId('correction-reason').fill('真实首稿');
    await dialog.getByTestId('correction-save-draft').focus(); await page.keyboard.press('Enter');
    await expect(dialog.getByTestId('correction-text')).toHaveText('真实浏览器更正');
    await expect(dialog.getByTestId('correction-original')).toHaveText(fixture.original);
    const firstResponse = await request.get(api + root + '/workspace', { headers }); expect(firstResponse.status()).toBe(200);
    const first = await firstResponse.json(); expect(first.headVersion).toBe(1);
    await dialog.getByTestId('correction-edit-part').click(); await dialog.getByTestId('correction-draft-text').fill('冲突后保留的真实草稿');
    await dialog.getByTestId('correction-reason').fill('真实竞争');
    const external = await request.post(api + root + '/revisions', { headers, data: { requestKey: crypto.randomUUID(),
      expected: { documentVersion: first.documentVersion, sourceId: first.sourceId, revisionId: first.revisionId, headVersion: first.headVersion, sourceHash: first.sourceHash, payloadHash: first.payloadHash },
      reason: '另一真实请求提交', operation: { kind: 'replace', blockId: first.block.id, partId: first.block.parts[0].id, text: '真实并发更正', offset: null, excluded: null } } });
    expect(external.status()).toBe(200);
    await dialog.getByTestId('correction-save-draft').click(); await expect(dialog.getByTestId('correction-conflict')).toBeVisible();
    await expect(dialog.getByTestId('correction-draft-text')).toHaveValue('冲突后保留的真实草稿');
    await dialog.getByTestId('correction-refresh').click(); await expect(dialog.getByTestId('correction-text')).toHaveText('真实并发更正');
    await expect(dialog.getByTestId('correction-draft-text')).toHaveValue('冲突后保留的真实草稿');
    await dialog.getByTestId('correction-save-draft').click(); await expect(dialog.getByTestId('correction-text')).toHaveText('冲突后保留的真实草稿');
    await dialog.getByTestId('correction-history').click(); await dialog.getByRole('button', { name: '预览修订 1', exact: true }).click();
    await expect(dialog.getByTestId('correction-history-view')).toBeVisible();
    await dialog.getByTestId('correction-reason').fill('真实历史恢复'); await dialog.getByTestId('correction-restore').click();
    await expect(dialog.getByTestId('correction-text')).toHaveText('真实浏览器更正');
    await dialog.getByTestId('correction-append').click(); await dialog.getByTestId('correction-draft-text').fill('真实人工补写');
    await dialog.getByTestId('correction-reason').fill('补写来源标记'); await dialog.getByTestId('correction-save-draft').click();
    await expect(dialog.getByTestId('correction-page')).toHaveText('1 / 2'); await dialog.getByTestId('correction-next').click();
    await expect(dialog.getByTestId('correction-text')).toHaveText('真实人工补写'); await expect(dialog.getByTestId('correction-source')).toHaveCount(0);
    await dialog.getByTestId('correction-reason').fill('真实噪声排除'); await dialog.getByTestId('correction-exclude').click();
    await expect(dialog).toContainText('已排除，后续索引不使用');
    const bytes = await request.get(api + path + `/versions/${versionId}/source`, { headers }); expect(bytes.status()).toBe(200); expect(await bytes.text()).toBe(fixture.original);
    expect(await dialog.evaluate(el => el.scrollWidth <= el.clientWidth + 1)).toBe(true);
    expect((await new AxeBuilder({ page }).include('.knowledge-correction-dialog').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    await page.screenshot({ path: testInfo.outputPath('real-correction.png'), fullPage: true });
    // 实际删除权威来源后，刷新不能继续显示原文或未保存草稿。
    await dialog.getByTestId('correction-edit-part').click(); await dialog.getByTestId('correction-draft-text').fill('来源失效后必须销毁');
    const current = await request.get(api + path, { headers }); expect(current.status()).toBe(200);
    const removed = await request.delete(api + path, { headers, data: { version: (await current.json()).version } }); expect(removed.ok()).toBe(true);
    await dialog.getByTestId('correction-refresh').click(); await expect(dialog.getByTestId('correction-error')).toBeVisible();
    await expect(dialog.getByTestId('correction-local-draft')).toHaveCount(0); await expect(dialog.getByTestId('correction-text')).toHaveCount(0);
    expect(errors).toEqual([]);
  });
}
