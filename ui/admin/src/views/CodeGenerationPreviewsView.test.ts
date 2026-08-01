import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { ElMessageBox } from 'element-plus';
import CodeGenerationPreviewsView from './CodeGenerationPreviewsView.vue';
import {
  applyTrackedCodeGeneration,
  listCodeGenerationRuns,
  previewTrackedCodeGeneration
} from '../api/code-generation-runs';
import {
  createCodeGenerationTemplate,
  deleteCodeGenerationTemplate,
  listCodeGenerationTemplates,
  updateCodeGenerationTemplate
} from '../api/code-generation-templates';

const permissionState = vi.hoisted(() => ({
  grants: new Set<string>()
}));

vi.mock('../auth/session', () => ({
  useSessionStore: () => ({
    can: (permission: string) => permissionState.grants.has(permission)
  })
}));

vi.mock('../api/code-generation-runs', () => ({
  applyTrackedCodeGeneration: vi.fn(),
  listCodeGenerationRuns: vi.fn(),
  previewTrackedCodeGeneration: vi.fn()
}));
vi.mock('../api/code-generation-templates', () => ({
  createCodeGenerationTemplate: vi.fn(),
  deleteCodeGenerationTemplate: vi.fn(),
  listCodeGenerationTemplates: vi.fn(),
  updateCodeGenerationTemplate: vi.fn()
}));

const trackedPreviewMock = vi.mocked(previewTrackedCodeGeneration);
const applyMock = vi.mocked(applyTrackedCodeGeneration);
const listRunsMock = vi.mocked(listCodeGenerationRuns);
const listTemplatesMock = vi.mocked(listCodeGenerationTemplates);
const createTemplateMock = vi.mocked(createCodeGenerationTemplate);
const updateTemplateMock = vi.mocked(updateCodeGenerationTemplate);
const deleteTemplateMock = vi.mocked(deleteCodeGenerationTemplate);
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
    columns: [{
      databaseName: 'Id',
      clrPropertyName: 'Id',
      jsonPropertyName: 'id',
      scalarType: 'Uuid' as const,
      isNullable: false,
      maxLength: null,
      numericPrecision: null,
      numericScale: null
    }]
  },
  schemaSha256: 'a'.repeat(64),
  createdAtUtc: '2026-07-30T08:00:00Z',
  createdByUserId: '0198f36e-f7a7-7c52-9cbb-774e67411204',
  updatedAtUtc: null,
  updatedByUserId: null,
  version: 1
};

describe('Vue 代码生成预览页', () => {
  beforeEach(() => {
    permissionState.grants = new Set([
      'codegen.previews.read',
      'codegen.templates.read',
      'codegen.templates.write',
      'codegen.runs.read',
      'codegen.runs.execute',
      'codegen.runs.apply',
      'codegen.runs.rollback'
    ]);
    trackedPreviewMock.mockReset().mockResolvedValue({
      runId: '0198f36e-f7a7-7c52-9cbb-774e67411212',
      preview: {
        databaseTableName: 'acme_catalog_product',
        readPermission: 'catalog.products.read',
        writePermission: 'catalog.products.write',
        artifacts: [{
          path: 'backend/Product.g.cs',
          kind: 'backend',
          sha256: 'a'.repeat(64),
          content: '<img src=x onerror=alert(1)>'
        }]
      }
    });
    applyMock.mockReset().mockResolvedValue({
      runId: '0198f36e-f7a7-7c52-9cbb-774e67411213',
      previewRunId: '0198f36e-f7a7-7c52-9cbb-774e67411212',
      artifactCount: 1,
      changedArtifactCount: 1,
      manifestSha256: 'c'.repeat(64)
    });
    listRunsMock.mockReset().mockResolvedValue({
      items: [{
        id: '0198f36e-f7a7-7c52-9cbb-774e67411212',
        templateId: null,
        templateVersion: null,
        operationKind: 'preview',
        status: 'succeeded',
        moduleKey: 'catalog',
        entityKey: 'product',
        schemaSha256: 'a'.repeat(64),
        artifactCount: 8,
        manifestSha256: 'b'.repeat(64),
        errorCode: null,
        requestedByUserId: '0198f36e-f7a7-7c52-9cbb-774e67411211',
        startedAtUtc: '2026-07-31T05:00:00Z',
        finishedAtUtc: '2026-07-31T05:00:01Z',
        sourceApplyRunId: null
      }],
      page: 1,
      pageSize: 20,
      total: 1
    });
    listTemplatesMock.mockReset().mockResolvedValue({
      items: [template],
      page: 1,
      pageSize: 20,
      total: 1
    });
    createTemplateMock.mockReset().mockResolvedValue(template);
    updateTemplateMock.mockReset().mockResolvedValue({
      ...template,
      name: 'Updated',
      version: 2
    });
    deleteTemplateMock.mockReset().mockResolvedValue();
  });

  it('controls the template directory and write form independently', async () => {
    permissionState.grants = new Set([
      'codegen.previews.read',
      'codegen.templates.write'
    ]);
    const writeOnly = mount(CodeGenerationPreviewsView);
    await flushPromises();

    expect(writeOnly.find('[data-testid="codegen-template-load"]').exists())
      .toBe(false);
    expect(writeOnly.find('[data-testid="codegen-template-save"]').exists())
      .toBe(true);
    expect(listTemplatesMock).not.toHaveBeenCalled();
    writeOnly.unmount();

    permissionState.grants = new Set([
      'codegen.previews.read',
      'codegen.templates.read'
    ]);
    const readOnly = mount(CodeGenerationPreviewsView);
    await flushPromises();

    expect(readOnly.find('[data-testid="codegen-template-load"]').exists())
      .toBe(true);
    expect(readOnly.find('[data-testid="codegen-template-save"]').exists())
      .toBe(false);
  });

  it('提交示例 Schema 并以纯文本显示生成产物', async () => {
    const wrapper = mount(CodeGenerationPreviewsView);

    await wrapper.get('[data-testid="codegen-preview"]').trigger('click');
    await flushPromises();

    expect(trackedPreviewMock).toHaveBeenCalledOnce();
    expect(trackedPreviewMock).toHaveBeenCalledWith(
      expect.objectContaining({ schema: expect.any(Object) })
    );
    expect(wrapper.get('[data-testid="codegen-content"]').text())
      .toBe('<img src=x onerror=alert(1)>');
    expect(wrapper.find('[data-testid="codegen-content"] img').exists())
      .toBe(false);
    expect(wrapper.text()).toContain('acme_catalog_product');
  });

  it('无效 JSON 在客户端失败关闭且不发送请求', async () => {
    const wrapper = mount(CodeGenerationPreviewsView);
    await wrapper.get('textarea[data-testid="codegen-schema"]')
      .setValue('{invalid');

    await wrapper.get('[data-testid="codegen-preview"]').trigger('click');
    await flushPromises();

    expect(trackedPreviewMock).not.toHaveBeenCalled();
    expect(wrapper.get('[role="alert"]').text())
      .toContain('client.codegen_invalid_json');
  });

  it('loads a source-free run history only with read permission', async () => {
    const wrapper = mount(CodeGenerationPreviewsView);
    await flushPromises();

    expect(listRunsMock).toHaveBeenCalledOnce();
    const history = wrapper.get('[data-testid="codegen-run-history"]').text();
    expect(history).toContain('product');
    expect(history).toContain('aaaaaaaaaaaa');
    expect(history).toContain('bbbbbbbbbbbb');
    expect(history).toContain('0198f36e-f7a7-7c52-9cbb-774e67411211');
    expect(history).toContain('2026-07-31T05:00:01Z');
    expect(history)
      .not.toContain('generated source');

    wrapper.unmount();
    listRunsMock.mockClear();
    permissionState.grants = new Set([
      'codegen.previews.read',
      'codegen.runs.execute'
    ]);
    const executeOnly = mount(CodeGenerationPreviewsView);
    await flushPromises();

    expect(listRunsMock).not.toHaveBeenCalled();
    expect(executeOnly.find('[data-testid="codegen-run-history"]').exists())
      .toBe(false);
  });

  it('加载模板到编辑器并以服务端 Version 更新和删除', async () => {
    const wrapper = mount(CodeGenerationPreviewsView);
    await flushPromises();

    await wrapper.get('[data-testid="codegen-template-load"]').trigger('click');
    expect(wrapper.get('[data-testid="codegen-schema"]')
      .attributes('modelvalue')).toBeUndefined();
    expect((wrapper.get('[data-testid="codegen-schema"]')
      .element as HTMLTextAreaElement).value).toContain('"HostOnly"');

    await wrapper.get('[data-testid="codegen-template-name"]')
      .setValue('Updated');
    await wrapper.get('[data-testid="codegen-template-update"]')
      .trigger('click');
    await flushPromises();
    expect(updateTemplateMock).toHaveBeenCalledWith(
      template.id,
      expect.objectContaining({ name: 'Updated', version: 1 })
    );

    await wrapper.get('[data-testid="codegen-template-delete"]')
      .trigger('click');
    await flushPromises();
    expect(deleteTemplateMock).toHaveBeenCalledWith(template.id, 2);
  });

  it('requires confirmation before applying a template-backed preview', async () => {
    const confirm = vi.spyOn(ElMessageBox, 'confirm')
      .mockResolvedValue(undefined as never);
    const wrapper = mount(CodeGenerationPreviewsView);
    await flushPromises();

    expect(wrapper.get('[data-testid="codegen-apply"]')
      .attributes('disabled')).toBeDefined();
    await wrapper.get('[data-testid="codegen-template-load"]')
      .trigger('click');
    await wrapper.get('[data-testid="codegen-preview"]').trigger('click');
    await flushPromises();

    expect(trackedPreviewMock).toHaveBeenCalledWith({
      templateId: template.id,
      templateVersion: template.version
    });
    expect(wrapper.get('[data-testid="codegen-apply"]')
      .attributes('disabled')).toBeUndefined();

    await wrapper.get('[data-testid="codegen-apply"]').trigger('click');
    await flushPromises();

    expect(confirm).toHaveBeenCalledOnce();
    expect(applyMock).toHaveBeenCalledWith({
      previewRunId: '0198f36e-f7a7-7c52-9cbb-774e67411212'
    });
    confirm.mockRestore();
  });
});
