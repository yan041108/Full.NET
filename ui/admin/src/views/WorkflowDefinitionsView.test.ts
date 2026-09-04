import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { defineComponent, h, type Component } from 'vue';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { useSessionStore } from '../auth/session';
import {
  getWorkflowStartForm,
  listWorkflowDefinitions,
  listWorkflowDefinitionVersions,
  startWorkflowInstance
} from '../api/workflow-runtime';
import {
  createWorkflowDefinition,
  getWorkflowDefinition,
  getWorkflowNodeTypeCatalog,
  publishWorkflowDefinition,
  updateWorkflowDefinitionDraft
} from '../api/workflow-definitions';
import { listWorkflowForms } from '../api/workflow-forms';
import WorkflowDefinitionsView from './WorkflowDefinitionsView.vue';

vi.mock('../api/workflow-runtime', () => ({
  getWorkflowStartForm: vi.fn(),
  listWorkflowDefinitions: vi.fn(),
  listWorkflowDefinitionVersions: vi.fn(),
  startWorkflowInstance: vi.fn()
}));
vi.mock('../api/workflow-definitions', () => ({
  createWorkflowDefinition: vi.fn(),
  getWorkflowDefinition: vi.fn(),
  getWorkflowNodeTypeCatalog: vi.fn(),
  publishWorkflowDefinition: vi.fn(),
  updateWorkflowDefinitionDraft: vi.fn()
}));
vi.mock('../api/workflow-forms', () => ({ listWorkflowForms: vi.fn() }));

const definition = {
  id: '01912345-6789-7abc-8def-0123456789ab',
  definitionKey: 'purchase.approval',
  draft: {
    schemaVersion: 1,
    nodes: [
      { nodeKey: 'start', nodeTypeKey: 'start', nodeSchemaVersion: 1, config: { nextNodeKeys: ['approval'] } },
      { nodeKey: 'approval', nodeTypeKey: 'human.approval', nodeSchemaVersion: 1, config: { nextNodeKeys: ['end'] } },
      { nodeKey: 'end', nodeTypeKey: 'end', nodeSchemaVersion: 1, config: { nextNodeKeys: [] } }
    ]
  },
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

function mountWithPermissions(
  permissions: string[],
  designerStub: Component = WorkflowVue3DesignerStub
) {
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
  return mount(WorkflowDefinitionsView, {
    global: {
      plugins: [pinia],
      stubs: { WorkflowVue3Designer: designerStub }
    }
  });
}

const WorkflowVue3DesignerStub = defineComponent({
  name: 'WorkflowVue3Designer',
  setup(_, { expose }) {
    expose({ readDraft: () => definition.draft });
    return () => h('div', { 'data-testid': 'workflow-vue3-designer-stub' });
  }
});

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
    vi.mocked(createWorkflowDefinition).mockReset();
    vi.mocked(getWorkflowDefinition).mockReset().mockResolvedValue(definition);
    vi.mocked(getWorkflowNodeTypeCatalog).mockReset().mockResolvedValue({
      catalogVersion: 1,
      definitionSchemaVersion: 1,
      nodeTypes: ['start', 'human.approval', 'notify.cc', 'gateway.exclusive', 'end'].map(nodeTypeKey => ({
        nodeTypeKey,
        nodeSchemaVersion: 1,
        designable: true,
        publishable: !['notify.cc', 'gateway.exclusive'].includes(nodeTypeKey),
        executable: !['notify.cc', 'gateway.exclusive'].includes(nodeTypeKey),
        supportsFieldPolicies: nodeTypeKey === 'human.approval'
      }))
    });
    vi.mocked(listWorkflowForms).mockReset().mockResolvedValue([{
      id: '01912345-6789-7abc-8def-0123456789a4',
      formKey: 'purchase.form',
      draft: { schemaVersion: 1, adapterVersion: 1, sections: [] },
      draftRevision: 1,
      latestPublishedVersionId: version.formVersionId,
      createdAtUtc: version.publishedAtUtc,
      updatedAtUtc: null
    }]);
    vi.mocked(updateWorkflowDefinitionDraft).mockReset().mockResolvedValue({ ...definition, draftRevision: 3 });
    vi.mocked(publishWorkflowDefinition).mockReset().mockResolvedValue(version);
  });

  it('真实挂载管理页时不依赖宿主的 Element Plus 全局注册', async () => {
    const warning = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    try {
      mountWithPermissions(['workflow.definitions.read']);
      await flushPromises();

      expect(warning.mock.calls.flat().join('\n')).not.toContain('Failed to resolve component: el-');
    } finally {
      warning.mockRestore();
    }
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

  it('通过 Workflow-Vue3 保存权威 Draft 并绑定已发布表单版本', async () => {
    const wrapper = mountWithPermissions([
      'workflow.definitions.read',
      'workflow.definitions.update',
      'workflow.definitions.publish',
      'workflow.forms.read'
    ]);
    await flushPromises();
    await wrapper.get('[data-testid="workflow-definition-edit"]').trigger('click');
    await flushPromises();

    expect(wrapper.find('[data-testid="workflow-vue3-designer-stub"]').exists()).toBe(true);
    await wrapper.get('[data-testid="workflow-definition-save"]').trigger('click');
    await flushPromises();
    expect(updateWorkflowDefinitionDraft).toHaveBeenCalledWith(definition.id, 2, definition.draft);

    await wrapper.get('[data-testid="workflow-definition-publish"]').trigger('click');
    await flushPromises();
    expect(publishWorkflowDefinition).toHaveBeenCalledWith(definition.id, 3, version.formVersionId);
  });

  it('设计器实例尚未就绪时失败关闭且不覆盖权威 Draft', async () => {
    const wrapper = mountWithPermissions(
      ['workflow.definitions.read', 'workflow.definitions.update'],
      defineComponent(() => () => h('div', { 'data-testid': 'workflow-designer-not-ready' }))
    );
    await flushPromises();
    await wrapper.get('[data-testid="workflow-definition-edit"]').trigger('click');
    await flushPromises();
    await wrapper.get('[data-testid="workflow-definition-save"]').trigger('click');
    await flushPromises();

    expect(updateWorkflowDefinitionDraft).not.toHaveBeenCalled();
    expect(wrapper.get('[role="alert"]').text()).toContain('client.workflow_designer_not_ready');
  });

  it('仅有定义编辑权限时不读取表单目录且仍可打开设计器', async () => {
    const wrapper = mountWithPermissions([
      'workflow.definitions.read',
      'workflow.definitions.update'
    ]);
    await flushPromises();
    await wrapper.get('[data-testid="workflow-definition-edit"]').trigger('click');
    await flushPromises();

    expect(wrapper.find('[data-testid="workflow-vue3-designer-stub"]').exists()).toBe(true);
    expect(listWorkflowForms).not.toHaveBeenCalled();
    expect(wrapper.find('[data-testid="workflow-definition-publish"]').exists()).toBe(false);
  });

  it('发布成功后的权威状态刷新失败时转成稳定 ProblemDetails', async () => {
    vi.mocked(getWorkflowDefinition)
      .mockResolvedValueOnce(definition)
      .mockRejectedValueOnce(new Error('client.workflow_definition_refresh_failed'));
    const wrapper = mountWithPermissions([
      'workflow.definitions.read',
      'workflow.definitions.update',
      'workflow.definitions.publish',
      'workflow.forms.read'
    ]);
    await flushPromises();
    await wrapper.get('[data-testid="workflow-definition-edit"]').trigger('click');
    await flushPromises();
    await wrapper.get('[data-testid="workflow-definition-publish"]').trigger('click');
    await flushPromises();

    expect(publishWorkflowDefinition).toHaveBeenCalled();
    expect(wrapper.get('[role="alert"]').text()).toContain('client.workflow_failed');
  });
});
