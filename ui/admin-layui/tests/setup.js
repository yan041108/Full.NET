import { beforeEach } from 'vitest';
import { adminI18n } from '../js/core/i18n.js';

beforeEach(() => {
  localStorage.clear();
  adminI18n.setLocale('zh-CN');
});
