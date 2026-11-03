import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

const challengeId = '01900000-0000-7000-8000-000000000001';
const json = (route, body, status = 200) => route.fulfill({ status, contentType: status >= 400 ? 'application/problem+json' : 'application/json', body: JSON.stringify(body) });

for (const mode of ['register', 'recover']) {
  for (const width of [1440, 390]) {
    test(`邮箱验证码重发、失败恢复与提交 ${mode} ${width}`, async ({ page }, testInfo) => {
      await page.setViewportSize({ width, height: 900 });
      await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
      const errors = [];
      page.on('pageerror', error => errors.push(error.message));
      let attempts = 0;
      let release;
      let submitted;
      await page.route('**/api/v1/**', async route => {
        const path = new URL(route.request().url()).pathname;
        if (path === (mode === 'register' ? '/api/v1/auth/register/email-challenge' : '/api/v1/auth/recover-password')) {
          attempts++;
          expect(route.request().headers().authorization).toBeUndefined();
          expect(route.request().headers()['accept-language']).toBe('zh-CN');
          expect(route.request().postDataJSON()).not.toHaveProperty('tenantId');
          if (attempts === 1) await new Promise(resolve => { release = resolve; });
          if (attempts === 2) return json(route, { type: 'about:blank', status: 503, title: '受控邮件暂不可用', code: 'identity.account_challenge_delivery_failed' }, 503);
          return json(route, { challengeId, expiresAtUtc: '2030-01-01T00:00:00Z' });
        }
        if (path === (mode === 'register' ? '/api/v1/auth/register' : '/api/v1/auth/recover-password/confirm')) {
          submitted = route.request().postDataJSON();
          return mode === 'register' ? json(route, { userId: challengeId }, 201) : route.fulfill({ status: 204 });
        }
        return json(route, { type: 'about:blank', status: 401, title: 'Anonymous', code: 'identity.session_not_active' }, 401);
      });
      await page.goto(mode === 'register' ? '/#/register' : '/#/recover-password');
      const form = page.locator(mode === 'register' ? '.register-form' : '.recover-form');
      const send = form.getByRole('button', { name: '发送验证码', exact: true });
      const submit = form.getByRole('button', { name: mode === 'register' ? '注册' : '更新密码', exact: true });
      await expect(send).toBeDisabled();
      await form.getByRole('textbox', { name: '邮箱', exact: true }).fill('old@example.test');
      await send.click();
      await expect.poll(() => attempts).toBe(1);
      await expect(send).toHaveClass(/is-loading/);
      await expect(submit).toBeDisabled();
      await form.getByRole('textbox', { name: '邮箱', exact: true }).fill('current@example.test');
      release();
      await expect(send).not.toHaveClass(/is-loading/);
      await form.getByRole('textbox', { name: '邮箱验证码', exact: true }).fill('123456');
      await expect(submit).toBeDisabled();
      await send.click();
      await expect(page.getByRole('alert').filter({ hasText: '受控邮件暂不可用' })).toBeVisible();
      await expect(form.getByRole('textbox', { name: '邮箱验证码', exact: true })).toHaveValue('');
      await send.click();
      await expect.poll(() => attempts).toBe(3);
      await expect(send).not.toHaveClass(/is-loading/);
      if (mode === 'register') await form.getByRole('textbox', { name: '显示名称', exact: true }).fill('验证码测试');
      await form.getByRole('textbox', { name: '邮箱验证码', exact: true }).fill('123456');
      await form.getByLabel(mode === 'register' ? '密码' : '新密码', { exact: true }).fill('FullNet!2026Secure');
      await expect(submit).toBeEnabled();
      expect((await new AxeBuilder({ page }).include(mode === 'register' ? '.register-form' : '.recover-form')
        .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze()).violations).toEqual([]);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      await page.screenshot({ path: testInfo.outputPath(`${mode}-${width}.png`), fullPage: true });
      await submit.focus();
      await page.keyboard.press('Enter');
      await expect.poll(() => submitted?.challengeId).toBe(challengeId);
      expect(submitted.challengeCode).toBe('123456');
      if (mode === 'register') expect(submitted.email).toBe('current@example.test');
      await expect(page).toHaveURL(/#\/login$/);
      expect(errors).toEqual([]);
    });
  }
}
