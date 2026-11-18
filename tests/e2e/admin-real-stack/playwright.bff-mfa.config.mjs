import base from './playwright.bff.config.mjs';

// MFA 夹具持有一次性材料，失败时不保留可能包含验证码的浏览器截图。
export default {
  ...base,
  testMatch: 'browser-mfa.spec.mjs',
  use: { ...base.use, screenshot: 'off' }
};
