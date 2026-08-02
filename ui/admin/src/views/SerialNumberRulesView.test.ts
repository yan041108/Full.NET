import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import SerialNumberRulesView from './SerialNumberRulesView.vue';
import { useSessionStore } from '../auth/session';
import {
  createSerialNumberRule,
  disableSerialNumberRule,
  enableSerialNumberRule,
  listSerialNumberRules,
  previewSerialNumber,
  updateSerialNumberRule
} from '../api/serial-number-rules';

vi.mock('../api/serial-number-rules', () => ({
  createSerialNumberRule: vi.fn(),
  disableSerialNumberRule: vi.fn(),
  enableSerialNumberRule: vi.fn(),
  listSerialNumberRules: vi.fn(),
  previewSerialNumber: vi.fn(),
  updateSerialNumberRule: vi.fn()
}));

const listMock = vi.mocked(listSerialNumberRules);
const rule = {
  id: '0198f36e-f7a7-7c52-9cbb-774e67411205',
  ruleKey: 'invoice.host',
  displayName: 'Invoice serial',
  description: null,
  scope: 1 as const,
  resetInterval: 1 as const,
  pattern: 'INV-{utc:yyyy}-{tenant}-{sequence:5}',
  minimumValue: 1,
  maximumValue: 99999,
  displayOrder: 10,
  isEnabled: true,
  createdAtUtc: '2026-07-30T08:00:00Z',
  createdByUserId: '0198f36e-f7a7-7c52-9cbb-774e67411204',
  updatedAtUtc: null,
  updatedByUserId: null,
  version: 1
};

function mountWithPermissions(permissions: string[]) {
  const pinia = createPinia();
  setActivePinia(pinia);
  const session = useSessionStore();
  session.currentUser = {
    id: '019bc2b1-2a40-7cc3-8992-a80de51bf296',
    username: 'admin',
    displayName: '管理员',
    tenantId: null,
    actorScope: 'host',
    scope: 'host',
    isSuperAdministrator: false,
    permissions,
    sessionId: '019bc2b1-2a40-7cc3-8992-a80de51bf297',
    preferredLocale: 'zh-CN',
    profileVersion: 1
  };
  return mount(SerialNumberRulesView, { global: { plugins: [pinia] } });
}

describe('Vue 流水号规则页', () => {
  beforeEach(() => {
    listMock.mockReset().mockResolvedValue({
      items: [rule],
      page: 1,
      pageSize: 20,
      total: 1
    });
    vi.mocked(createSerialNumberRule).mockReset();
    vi.mocked(updateSerialNumberRule).mockReset();
    vi.mocked(enableSerialNumberRule).mockReset();
    vi.mocked(disableSerialNumberRule).mockReset();
    vi.mocked(previewSerialNumber).mockReset();
  });

  it('仅有 read 时不显示写入与预览操作', async () => {
    const wrapper = mountWithPermissions(['serial_numbers.rules.read']);
    await flushPromises();
    expect(wrapper.find('[data-testid="serial-rule-create"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="serial-rule-save"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="serial-rule-disable"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="serial-rule-preview"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="serial-rule-load"]').exists()).toBe(true);
  });

  it('create-only 只显示创建按钮', async () => {
    const wrapper = mountWithPermissions(['serial_numbers.rules.read', 'serial_numbers.rules.create']);
    await flushPromises();
    expect(wrapper.find('[data-testid="serial-rule-create"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="serial-rule-save"]').exists()).toBe(false);
  });

  it('update-only 在选中规则后只显示保存按钮', async () => {
    const wrapper = mountWithPermissions(['serial_numbers.rules.read', 'serial_numbers.rules.update']);
    await flushPromises();
    await wrapper.get('[data-testid="serial-rule-load"]').trigger('click');
    expect(wrapper.find('[data-testid="serial-rule-save"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="serial-rule-create"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="serial-rule-disable"]').exists()).toBe(false);
  });

  it('disable-only 在选中规则后只显示禁用按钮', async () => {
    const wrapper = mountWithPermissions(['serial_numbers.rules.read', 'serial_numbers.rules.disable']);
    await flushPromises();
    await wrapper.get('[data-testid="serial-rule-load"]').trigger('click');
    expect(wrapper.find('[data-testid="serial-rule-disable"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="serial-rule-save"]').exists()).toBe(false);
  });

  it('preview-only 只显示预览按钮', async () => {
    const wrapper = mountWithPermissions(['serial_numbers.rules.read', 'serial_numbers.rules.preview']);
    await flushPromises();
    expect(wrapper.find('[data-testid="serial-rule-preview"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="serial-rule-create"]').exists()).toBe(false);
  });
});