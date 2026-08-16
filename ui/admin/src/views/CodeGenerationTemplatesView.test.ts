import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { ElMessageBox } from 'element-plus';
import CodeGenerationTemplatesView from './CodeGenerationTemplatesView.vue';
import { useSessionStore } from '../auth/session';
import {
  createCodeGenerationTemplate,
  deleteCodeGenerationTemplate,
  listCodeGenerationTemplates,
  updateCodeGenerationTemplate
} from '../api/code-generation-templates';

vi.mock('../api/code-generation-templates', () => ({
  createCodeGenerationTemplate: vi.fn(),
  deleteCodeGenerationTemplate: vi.fn(),
  listCodeGenerationTemplates: vi.fn(),
  updateCodeGenerationTemplate: vi.fn()
}));

vi.mock('../api/code-generation-catalog', () => ({
  listCodeGenerationCatalogTables: vi.fn().mockResolvedValue([]),
  listCodeGenerationCatalogColumns: vi.fn(),
  syncCodeGenerationCatalogColumns: vi.fn()
}));

const listMock = vi.mocked(listCodeGenerationTemplates);
const template = {
  id: '0198f36e-f7a7-7c52-9cbb-774e67411205',
  name: 'Product CRUD',
  description: null,
  schema: {
    ownerKey: 'acme',
    moduleKey: 'catalog',
    entityKey: 'product',
    databaseTableName: 'acme_catalog_product',
    rootNamespace: 'Acme.Modules.Catalog',
    clrTypeName: 'Product',
    apiResourceName: 'products',
    permissionResourceName: 'products',
    dataScope: 'HostOnly' as const,
    hasVersion: true,
    columns: []
  },
  schemaSha256: 'a'.repeat(64),
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
  return mount(CodeGenerationTemplatesView, { global: { plugins: [pinia] } });
}

describe('Vue 代码生成模板页', () => {
  beforeEach(() => {
    listMock.mockReset().mockResolvedValue({
      items: [template],
      page: 1,
      pageSize: 20,
      total: 1
    });
    vi.mocked(createCodeGenerationTemplate).mockReset();
    vi.mocked(updateCodeGenerationTemplate).mockReset();
    vi.mocked(deleteCodeGenerationTemplate).mockReset();
    vi.spyOn(ElMessageBox, 'confirm').mockResolvedValue(undefined as never);
  });

  it('删除模板前要求用户明确确认', async () => {
    vi.mocked(deleteCodeGenerationTemplate).mockResolvedValue(undefined);
    const wrapper = mountWithPermissions(['codegen.templates.read', 'codegen.templates.delete']);
    await flushPromises();
    await wrapper.get('[data-testid="codegen-template-load"]').trigger('click');
    await wrapper.get('[data-testid="codegen-template-delete"]').trigger('click');
    await flushPromises();

    expect(ElMessageBox.confirm).toHaveBeenCalledOnce();
    expect(deleteCodeGenerationTemplate).toHaveBeenCalledWith(template.id, template.version);
  });

  it('取消删除确认时不调用删除接口', async () => {
    vi.mocked(ElMessageBox.confirm).mockRejectedValueOnce('cancel');
    const wrapper = mountWithPermissions(['codegen.templates.read', 'codegen.templates.delete']);
    await flushPromises();
    await wrapper.get('[data-testid="codegen-template-load"]').trigger('click');
    await wrapper.get('[data-testid="codegen-template-delete"]').trigger('click');
    await flushPromises();

    expect(deleteCodeGenerationTemplate).not.toHaveBeenCalled();
    expect(wrapper.find('[role="alert"]').exists()).toBe(false);
  });

  it('删除确认未结束时拒绝重复打开确认框', async () => {
    let resolveConfirm!: () => void;
    vi.mocked(ElMessageBox.confirm).mockImplementationOnce(() => new Promise(resolve => {
      resolveConfirm = () => resolve(undefined as never);
    }));
    vi.mocked(deleteCodeGenerationTemplate).mockResolvedValue(undefined);
    const wrapper = mountWithPermissions(['codegen.templates.read', 'codegen.templates.delete']);
    await flushPromises();
    await wrapper.get('[data-testid="codegen-template-load"]').trigger('click');
    const deleteButton = wrapper.get('[data-testid="codegen-template-delete"]');
    await deleteButton.trigger('click');
    await deleteButton.trigger('click');

    expect(ElMessageBox.confirm).toHaveBeenCalledOnce();
    resolveConfirm();
    await flushPromises();
    expect(deleteCodeGenerationTemplate).toHaveBeenCalledOnce();
  });

  it('仅有 read 时不显示写入操作', async () => {
    const wrapper = mountWithPermissions(['codegen.templates.read']);
    await flushPromises();
    expect(wrapper.find('[data-testid="codegen-template-save"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="codegen-template-update"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="codegen-template-delete"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="codegen-template-load"]').exists()).toBe(true);
  });

  it('create-only 只显示保存按钮', async () => {
    const wrapper = mountWithPermissions(['codegen.templates.read', 'codegen.templates.create']);
    await flushPromises();
    expect(wrapper.find('[data-testid="codegen-template-save"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="codegen-template-update"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="codegen-template-delete"]').exists()).toBe(false);
  });

  it('update-only 在选中模板后只显示更新按钮', async () => {
    const wrapper = mountWithPermissions(['codegen.templates.read', 'codegen.templates.update']);
    await flushPromises();
    await wrapper.get('[data-testid="codegen-template-load"]').trigger('click');
    expect(wrapper.find('[data-testid="codegen-template-save"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="codegen-template-update"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="codegen-template-delete"]').exists()).toBe(false);
  });

  it('delete-only 在选中模板后只显示删除按钮', async () => {
    const wrapper = mountWithPermissions(['codegen.templates.read', 'codegen.templates.delete']);
    await flushPromises();
    await wrapper.get('[data-testid="codegen-template-load"]').trigger('click');
    expect(wrapper.find('[data-testid="codegen-template-delete"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="codegen-template-save"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="codegen-template-update"]').exists()).toBe(false);
  });

  it('列表加载失败时向用户显示稳定错误码', async () => {
    listMock.mockRejectedValue({
      type: 'about:blank',
      status: 503,
      code: 'codegen.templates.unavailable',
      title: 'Templates unavailable'
    });

    const wrapper = mountWithPermissions(['codegen.templates.read']);
    await flushPromises();

    expect(wrapper.get('[role="alert"]').text()).toContain('codegen.templates.unavailable');
  });
});
