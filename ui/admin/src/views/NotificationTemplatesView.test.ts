import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import NotificationTemplatesView from './NotificationTemplatesView.vue';
import { useSessionStore } from '../auth/session';
import {
  createNotificationTemplate,
  listNotificationProviderTypes,
  listNotificationTemplates,
  publishNotificationTemplate,
  updateNotificationTemplate
} from '../api/notification-platform';

vi.mock('../api/notification-platform', () => ({
  createNotificationTemplate: vi.fn(),
  listNotificationProviderTypes: vi.fn(),
  listNotificationTemplates: vi.fn(),
  publishNotificationTemplate: vi.fn(),
  updateNotificationTemplate: vi.fn()
}));

const listMock = vi.mocked(listNotificationTemplates);
const typesMock = vi.mocked(listNotificationProviderTypes);

const template = {
  id: '0198f36e-f7a7-7c52-9cbb-774e67411201',
  templateKey: 'order.shipped',
  localeTag: 'zh-CN',
  defaultLocaleTag: 'zh-CN',
  channelKey: 'inbox',
  contentCategoryKey: 'transactional',
  draftSubject: '已发货',
  draftBodyJson: '{"text":"订单已发货"}',
  draftParameterSchemaJson: '{"schemaVersion":1,"parameters":[]}',
  draftRevision: 1,
  latestPublishedVersionId: null,
  latestPublishedVersionNumber: null,
  latestContentHash: null,
  latestContentClassificationKey: null,
  publishedLocaleTags: [],
  missingLocaleTags: ['en-US'],
  createdAtUtc: '2026-08-31T00:00:00Z',
  updatedAtUtc: null,
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
  return mount(NotificationTemplatesView, { global: { plugins: [pinia] } });
}

describe('Vue 通知模板页', () => {
  beforeEach(() => {
    listMock.mockReset().mockResolvedValue({
      items: [template],
      page: 1,
      pageSize: 20,
      total: 1
    });
    typesMock.mockReset().mockResolvedValue([]);
    vi.mocked(createNotificationTemplate).mockReset();
    vi.mocked(updateNotificationTemplate).mockReset();
    vi.mocked(publishNotificationTemplate).mockReset();
  });

  it('仅有 read 时不显示创建、保存与发布', async () => {
    const wrapper = mountWithPermissions(['notifications.templates.read']);
    await flushPromises();

    expect(wrapper.find('[data-testid="notification-templates-create"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="notification-templates-save"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="notification-templates-publish"]').exists()).toBe(false);
  });

  it('create-only 只显示创建', async () => {
    const wrapper = mountWithPermissions([
      'notifications.templates.read',
      'notifications.templates.create'
    ]);
    await flushPromises();

    expect(wrapper.find('[data-testid="notification-templates-create"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="notification-templates-save"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="notification-templates-publish"]').exists()).toBe(false);
  });

  it('选中草稿后 update/publish 各自独立显示', async () => {
    const wrapper = mountWithPermissions([
      'notifications.templates.read',
      'notifications.templates.update',
      'notifications.templates.publish'
    ]);
    await flushPromises();
    await wrapper.get('[data-testid="notification-templates-load"]').trigger('click');
    await flushPromises();

    expect(wrapper.find('[data-testid="notification-templates-create"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="notification-templates-save"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="notification-templates-publish"]').exists()).toBe(true);
  });

  it('列表明确显示草稿或最新发布版本', async () => {
    listMock.mockResolvedValue({
      items: [
        template,
        {
          ...template,
          id: '0198f36e-f7a7-7c52-9cbb-774e67411202',
          templateKey: 'order.delivered',
          latestPublishedVersionId: '0198f36e-f7a7-7c52-9cbb-774e67411203',
          latestPublishedVersionNumber: 2,
          latestContentHash: 'sha256:published',
          latestContentClassificationKey: 'c1'
        }
      ],
      page: 1,
      pageSize: 20,
      total: 2
    });

    const wrapper = mountWithPermissions(['notifications.templates.read']);
    await flushPromises();

    const states = wrapper.findAll('[data-testid="notification-templates-state"]');
    expect(states).toHaveLength(2);
    expect(states[0].text()).toContain('草稿');
    expect(states[1].text()).toContain('已发布 v2');
  });

  it('列表展示语言标签并在选中后提示缺失语言', async () => {
    const wrapper = mountWithPermissions([
      'notifications.templates.read',
      'notifications.templates.update'
    ]);
    await flushPromises();

    expect(wrapper.get('[data-testid="notification-templates-locale-tag"]').text()).toBe('zh-CN');
    await wrapper.get('[data-testid="notification-templates-load"]').trigger('click');
    await flushPromises();

    expect(wrapper.get('[data-testid="notification-templates-locale-hints"]').exists()).toBe(true);
    expect(wrapper.findAll('[data-testid="notification-templates-missing-locale"]')).toHaveLength(1);
    expect(wrapper.get('[data-testid="notification-templates-missing-locale"]').text()).toBe('en-US');
  });

  it('创建草稿时提交所选语言版本', async () => {
    vi.mocked(createNotificationTemplate).mockResolvedValue({
      ...template,
      localeTag: 'en-US',
      defaultLocaleTag: 'en-US'
    });
    const wrapper = mountWithPermissions([
      'notifications.templates.read',
      'notifications.templates.create'
    ]);
    await flushPromises();

    await wrapper
      .get('[data-testid="notification-templates-locale"]')
      .findComponent({ name: 'ElSelect' })
      .setValue('en-US');
    await wrapper
      .get('[data-testid="notification-templates-default-locale"]')
      .findComponent({ name: 'ElSelect' })
      .setValue('en-US');
    await wrapper.get('[data-testid="notification-templates-key"]').setValue('order.shipped.en');
    await wrapper.get('[data-testid="notification-templates-subject"]').setValue('Shipped');
    await wrapper.get('[data-testid="notification-templates-body"]').setValue('Your order shipped');
    await wrapper.get('[data-testid="notification-templates-create"]').trigger('click');
    await flushPromises();

    expect(createNotificationTemplate).toHaveBeenCalledWith({
      templateKey: 'order.shipped.en',
      localeTag: 'en-US',
      defaultLocaleTag: 'en-US',
      channelKey: 'inbox',
      contentCategoryKey: 'transactional',
      draftSubject: 'Shipped',
      draftBody: { text: 'Your order shipped' },
      parameterSchema: { schemaVersion: 1, parameters: [] }
    });
  });
});
