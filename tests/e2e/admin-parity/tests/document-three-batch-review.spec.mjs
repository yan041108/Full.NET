import { expect, test } from '@playwright/test';

// 受控 HTTP 只验浏览器对活动文本、下载与能力边界的处理，不替代真实双库验证。
for (const viewport of [{ width: 1440, height: 1000 }, { width: 390, height: 844 }]) {
  test(`三批复审：安全文本预览与超限下载 ${viewport.width}`, async ({ page }, testInfo) => {
    await page.setViewportSize(viewport);
    const errors = []; page.on('pageerror', error => errors.push(error.message));
    const json = (route, body) => route.fulfill({ contentType: 'application/json', body: JSON.stringify(body) });
    let office = false; let tasks = 0;
    const script = '<script>parent.localStorage.setItem("document-script-executed", "yes")</script>';
    await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
    await page.route('**/api/v1/document/public/shares/*/access', route => json(route, {
      shareId: '01912345-6789-7abc-8def-0123456789ab', documentId: '01912345-6789-7abc-8def-0123456789ab',
      shareCode: office ? 'office' : 'html', title: office ? '超限 Office 文档' : 'HTML 原文安全预览',
      fileName: office ? 'large.doc' : 'sample.html', mimeType: office ? 'application/msword' : 'text/html',
      fileSizeBytes: office ? 52428801 : script.length, hasPassword: false, accessCountRemaining: 3, previewGrant: 'review-session'
    }));
    await page.route('**/api/v1/document/public/preview-capabilities', route => json(route, {
      enabled: true, providerKey: 'external_http', allowedMimeTypes: ['application/msword'], maxInputBytes: 52428800, maxAttempts: 3
    }));
    await page.route('**/api/v1/document/public/shares/*/preview-tasks', route => { tasks++; return json(route, {}); });
    await page.route('**/api/v1/document/public/shares/*/content', route => route.fulfill({
      contentType: office ? 'application/msword' : 'text/html', body: office ? 'office-bytes' : script
    }));
    await page.goto('/#/document/share/html');
    const frame = page.frameLocator('iframe');
    await expect(frame.locator('body')).toContainText(script);
    expect(await page.evaluate(() => localStorage.getItem('document-script-executed'))).toBeNull();
    // 按钮必须真实触发独立下载请求；noopener 打开不能因为空句柄立即吊销 Blob。
    await page.getByTestId('document-public-share-download').focus();
    const request = page.waitForRequest(r => r.url().endsWith('/html/content') && r.postDataJSON().accessTypeKey === 'download');
    const popupPromise = page.waitForEvent('popup');
    await page.keyboard.press('Enter');
    const popup = await popupPromise;
    await expect(popup.locator('body')).toContainText(script);
    expect(await popup.evaluate(() => localStorage.getItem('document-script-executed'))).toBeNull();
    await popup.close();
    expect((await request).postDataJSON()).toMatchObject({ previewGrant: 'review-session', accessTypeKey: 'download' });
    office = true;
    await page.goto('/#/document/share/office');
    await expect(page.getByText('超限 Office 文档', { exact: true })).toBeVisible();
    await expect(page.getByTestId('document-public-share-download')).toBeVisible();
    await expect(page.locator('iframe')).toHaveCount(0);
    expect(tasks).toBe(0);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
    await page.screenshot({ path: testInfo.outputPath(`review-${viewport.width}.png`), fullPage: true });
    expect(errors).toEqual([]);
  });
}
