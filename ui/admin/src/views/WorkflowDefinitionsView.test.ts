import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { useSessionStore } from '../auth/session';
import {
  getWorkflowStartForm,
  listWorkflowDefinitions,
  listWorkflowDefinitionVersions,
  startWorkflowInstance
} from '../api/workflow-runtime';
import WorkflowDefinitionsView from './WorkflowDefinitionsView.vue';

vi.mock('../api/workflow-runtime', () => ({
  getWorkflowStartForm: vi.fn(),
  listWorkflowDefinitions: vi.fn(),
  listWorkflowDefinitionVersions: vi.fn(),
  startWorkflowInstance: vi.fn()
}));

const definition = {
  id: '01912345-6789-7abc-8def-0123456789ab',
  definitionKey: 'purchase.approval',
  draft: { schemaVersion: 1, nodes: [] },
  draftRevision: 2,
  latestPublishedVersionId: '01912345-6789-7abc-8def-0123456789ac',
  version: 2,
  createdAtUtc: '2026-08-30T00:00:00Z',
  updatedAtUtc: null
};

const version = {
  id: definition.latestPublishedVersionId,
  definitionId: definition.id,
  formVersionId: '01912345-6789-7abc-8def-0123456789ad',
  versionNumber: 1,
  schemaVersion: 1,
  canonicalJson: '{}',
  contentHash: 'hash',
  publishedById: '01912345-6789-7abc-8def-0123456789ae',
  publishedAtUtc: '2026-08-30T00:00:00Z'
};

function mountWithPermissions(permissions: string[]) {
  const pinia = createPinia();
  setActivePinia(pinia);
  const session = useSessionStore();
  session.currentUser = {
    id: '01912345-6789-7abc-8def-0123456789af',
    username: 'starter',
    displayName: '发起人',
    tenantId: '01912345-6789-7abc-8def-0123456789a1',
    actorScope: 'tenant',
    scope: 'tenant',
    isSuperAdministrator: false,
    permissions,
    sessionId: '01912345-6789-7abc-8def-0123456789a2',
    preferredLocale: 'zh-CN',
    profileVersion: 1
  };
  return mount(WorkflowDefinitionsView, { global: { plugins: [pinia] } });
}

describe('WorkflowDefinitionsView', () => {
  beforeEach(() => {
    vi.mocked(listWorkflowDefinitions).mockReset().mockResolvedValue([definition]);
    vi.mocked(listWorkflowDefinitionVersions).mockReset().mockResolvedValue([version]);
    vi.mocked(getWorkflowStartForm).mockReset().mockResolvedValue({
      version: {
        id: version.formVersionId,
        formDefinitionId: definition.id,
        versionNumber: 1,
        schemaVersion: 1,
        adapterVersion: 1,
        componentCatalogVersion: 1,
        formSchemaJson: '{}',
        webRenderSchemaJson: '{}',
        contentHash: 'form-hash',
        publishedById: version.publishedById,
        publishedAtUtc: version.publishedAtUtc
      },
      schema: {
        schemaVersion: 1,
        adapterVersion: 1,
        sections: [{
          sectionKey: 'request',
          fields: [{
            fieldKey: 'summary',
            fieldTypeKey: 'text',
            required: true,
            constraints: {}
          }]
        }]
      }
    });
    vi.mocked(startWorkflowInstance).mockReset();
  });

  it('没有实例发起权限时不创建发起入口', async () => {
    const wrapper = mountWithPermissions(['workflow.definitions.read']);
    await flushPromises();
    await wrapper.get('[data-testid="workflow-definition-versions"]').trigger('click');
    await flushPromises();

    expect(wrapper.find('[data-testid="workflow-definition-start"]').exists()).toBe(false);
  });

  it('使用已发布版本和静态表单发起实例', async () => {
    vi.mocked(startWorkflowInstance).mockResolvedValue({
      id: '01912345-6789-7abc-8def-0123456789a3',
      definitionVersionId: version.id,
      formVersionId: version.formVersionId,
      businessType: 'purchase',
      businessId: 'PO-001',
      statusKey: 'running',
      revision: 1,
      activeTodoId: null,
      startedAtUtc: '2026-08-30T00:00:00Z'
    });
    const wrapper = mountWithPermissions([
      'workflow.definitions.read',
      'workflow.instances.start'
    ]);
    await flushPromises();
    await wrapper.get('[data-testid="workflow-definition-versions"]').trigger('click');
    await flushPromises();
    await wrapper.get('[data-testid="workflow-definition-start"]').trigger('click');
    await flushPromises();
    await wrapper.get('[data-testid="workflow-business-type"]').setValue('purchase');
    await wrapper.get('[data-testid="workflow-business-id"]').setValue('PO-001');
    await wrapper.get('[data-field-key="summary"] input').setValue('采购审批');
    await wrapper.get('[data-testid="workflow-start-submit"]').trigger('click');
    await flushPromises();

    expect(startWorkflowInstance).toHaveBeenCalledWith(
      version.id,
      'purchase',
      'PO-001',
      { summary: '采购审批' },
      expect.any(String)
    );
  });
});
