import { beforeEach } from 'vitest';
import { useAdminI18n } from '../i18n/adminI18n';

beforeEach(() => {
  localStorage.clear();
  useAdminI18n().setLocale('zh-CN');
});
