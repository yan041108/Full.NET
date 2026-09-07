import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import PrintingPreviewView from './PrintingPreviewView.vue';
import { useSessionStore } from '../auth/session';
import { ElSelect } from 'element-plus';
import { listPrintingTemplates, previewPrintingTemplate } from '../api/printing-templates';

vi.mock('../api/printing-templates', () => ({
  listPrintingTemplates: vi.fn(),
  createPrintingTemplate: vi.fn(),
  publishPrintingTemplate: vi.fn(),
  previewPrintingTemplate: vi.fn()
}));

const listMock = vi.mocked(listPrintingTemplates);

function mountWithPermissions(permissions: string[]) {
  const pinia = createPinia();
  setActivePinia(pinia);
  const session = useSessionStore();
  session.currentUser = {
    id: '019bc2b1-2a40-7cc3-8992-a80de51bf296',
    username: 'admin',
    displayName: '管理员',
    tenantId: '019bc2b1-2a40-7cc3-8992-a80de51bf297',
    actorScope: 'tenant',
    scope: 'tenant',
    isSuperAdministrator: false,
    passwordChangeRequired: false,
    permissions,
    sessionId: '019bc2b1-2a40-7cc3-8992-a80de51bf298',
    preferredLocale: 'zh-CN',
    profileVersion: 1
  };
  return mount(PrintingPreviewView, { global: { plugins: [pinia] } });
}

describe('PrintingPreviewView', () => {
  beforeEach(() => {
    listMock.mockResolvedValue([]);
  });

  it('shows preview action entry point', async () => {
    const wrapper = mountWithPermissions(['printing.templates.preview']);
    await flushPromises();
    expect(wrapper.find('[data-testid="printing-preview-run"]').exists()).toBe(true);
  });

  it('removes executable template markup before inserting preview into the admin DOM', async () => {
    vi.mocked(previewPrintingTemplate).mockResolvedValue({
      templateId: '019bc2b1-2a40-7cc3-8992-a80de51bf299',
      templateKey: 'review-template',
      templateName: '测试模板',
      versionNumber: 1,
      formSchemaKey: 'tenant-profile',
      boundFields: {},
      html: '<div style="color:red">安全正文</div><img src="x" onerror="alert(1)"><svg onload="alert(2)"></svg><a href="javascript:alert(3)">链接</a>',
      generatedAtUtc: '2026-09-07T00:00:00Z'
    });
    const wrapper = mountWithPermissions(['printing.templates.preview']);
    await flushPromises();
    wrapper.findComponent(ElSelect).vm.$emit('update:modelValue', '019bc2b1-2a40-7cc3-8992-a80de51bf299');
    await flushPromises();
    await wrapper.get('[data-testid="printing-preview-run"]').trigger('click');
    await flushPromises();
    const surface = wrapper.get('.printing-preview-html');
    expect(surface.text()).toContain('安全正文');
    expect(surface.find('[onerror], [onload], svg, [href^="javascript:"]').exists()).toBe(false);
    expect(surface.find('div').attributes('style')).toContain('color');
  });
});
