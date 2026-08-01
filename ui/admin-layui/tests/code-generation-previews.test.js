import { describe, expect, it, vi } from 'vitest';
import { createCodeGenerationPreviewsController } from '../js/core/code-generation-previews.js';

describe('Layui 代码生成预览控制器', () => {
  it('提交显式 Schema 并用安全 DOM 呈现只读产物', async () => {
    document.body.innerHTML = `
      <div data-codegen-problem hidden><strong></strong><span></span></div>
      <form data-codegen-form>
        <textarea name="schema"></textarea>
        <button type="submit">preview</button>
      </form>
      <div data-codegen-summary></div>
      <div data-codegen-artifacts></div>
      <pre data-codegen-content><code></code></pre>
    `;
    const input = {
      ownerKey: 'acme',
      moduleKey: 'catalog',
      entityKey: 'product',
      databaseTableName: 'acme_catalog_product',
      rootNamespace: 'Acme.Modules.Catalog',
      clrTypeName: 'Product',
      apiResourceName: 'products',
      permissionResourceName: 'products',
      dataScope: 'TenantRequired',
      hasVersion: true,
      columns: [{
        databaseName: 'Id',
        clrPropertyName: 'Id',
        jsonPropertyName: 'id',
        scalarType: 'Uuid',
        isNullable: false,
        maxLength: null,
        numericPrecision: null,
        numericScale: null
      }]
    };
    document.querySelector('[name="schema"]').value = JSON.stringify(input);
    const request = vi.fn().mockResolvedValue({
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
    const controller = createCodeGenerationPreviewsController(document, {
      request,
      translation: () => ({ t: key => key }),
      hasPermission: permission => permission === 'codegen.runs.execute'
    });

    document.querySelector('[data-codegen-form]').requestSubmit();
    await vi.waitFor(() => expect(request).toHaveBeenCalledOnce());

    expect(request).toHaveBeenCalledWith(
      '/api/v1/code-generation/runs/preview',
      expect.objectContaining({ method: 'POST' })
    );
    await vi.waitFor(() =>
      expect(
        document.querySelector('[data-codegen-content] code').textContent
      ).toBe('<img src=x onerror=alert(1)>'));
    expect(document.querySelector('[data-codegen-content] img')).toBeNull();
    controller.dispose();
  });

  it('仅在读取权限存在时以纯文本呈现运行摘要', async () => {
    document.body.innerHTML = `
      <div data-codegen-run-history></div>
    `;
    const request = vi.fn().mockResolvedValue({
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
    const controller = createCodeGenerationPreviewsController(document, {
      request,
      translation: () => ({ t: key => key }),
      hasPermission: permission => permission === 'codegen.runs.read'
    });

    await controller.load();

    const history = document.querySelector(
      '[data-codegen-run-history]'
    ).textContent;
    expect(history).toContain('product');
    expect(history).toContain('aaaaaaaaaaaa');
    expect(history).toContain('bbbbbbbbbbbb');
    expect(history).toContain('0198f36e-f7a7-7c52-9cbb-774e67411211');
    expect(history).toContain('2026-07-31T05:00:01Z');
    expect(history)
      .not.toContain('generated source');
    controller.dispose();
  });

  it('only applies a reviewed template preview after confirmation', async () => {
    document.body.innerHTML = `
      <div data-codegen-problem hidden><strong></strong><span></span></div>
      <form data-codegen-template-form>
        <input name="templateName">
        <textarea name="templateDescription"></textarea>
      </form>
      <div data-codegen-template-directory></div>
      <form data-codegen-form>
        <textarea name="schema"></textarea>
        <button type="submit">preview</button>
      </form>
      <button type="button" data-codegen-apply>apply</button>
      <div data-codegen-summary></div>
      <div data-codegen-artifacts></div>
      <pre data-codegen-content><code></code></pre>
    `;
    const template = {
      id: '0198f36e-f7a7-7c52-9cbb-774e67411205',
      name: 'Product CRUD',
      description: null,
      version: 1,
      schemaSha256: 'c'.repeat(64),
      createdAtUtc: '2026-07-31T05:00:00Z',
      createdByUserId: '0198f36e-f7a7-7c52-9cbb-774e67411204',
      updatedAtUtc: null,
      updatedByUserId: null,
      schema: {
        ownerKey: 'acme', moduleKey: 'catalog', entityKey: 'product',
        databaseTableName: 'acme_catalog_product',
        rootNamespace: 'Acme.Modules.Catalog', clrTypeName: 'Product',
        apiResourceName: 'products', permissionResourceName: 'products',
        dataScope: 'HostOnly', hasVersion: true,
        columns: [{
          databaseName: 'Id', clrPropertyName: 'Id', jsonPropertyName: 'id',
          scalarType: 'Uuid', isNullable: false, maxLength: null,
          numericPrecision: null, numericScale: null
        }]
      }
    };
    const request = vi.fn()
      .mockResolvedValueOnce({
        items: [template], page: 1, pageSize: 20, total: 1
      })
      .mockResolvedValueOnce({
        runId: '0198f36e-f7a7-7c52-9cbb-774e67411212',
        preview: {
          databaseTableName: 'acme_catalog_product',
          readPermission: 'catalog.products.read',
          writePermission: 'catalog.products.write',
          artifacts: [{
            path: 'backend/Product.g.cs', kind: 'backend',
            sha256: 'a'.repeat(64), content: 'source'
          }]
        }
      })
      .mockResolvedValueOnce({
        runId: '0198f36e-f7a7-7c52-9cbb-774e67411213',
        previewRunId: '0198f36e-f7a7-7c52-9cbb-774e67411212',
        artifactCount: 1, changedArtifactCount: 1,
        manifestSha256: 'b'.repeat(64)
      });
    const confirm = vi.fn().mockResolvedValue(true);
    const controller = createCodeGenerationPreviewsController(document, {
      request,
      confirm,
      translation: () => ({ t: key => key }),
      hasPermission: permission => [
        'codegen.templates.read',
        'codegen.runs.execute',
        'codegen.runs.apply',
        'codegen.runs.rollback'
      ].includes(permission)
    });
    await controller.load();
    document.querySelector('[data-codegen-template-load]').click();
    document.querySelector('[data-codegen-form]').requestSubmit();
    await vi.waitFor(() => expect(request).toHaveBeenCalledTimes(2));
    await vi.waitFor(() => expect(
      document.querySelector('[data-codegen-apply]').disabled
    ).toBe(false));

    document.querySelector('[data-codegen-apply]').click();
    await vi.waitFor(() => expect(request).toHaveBeenCalledTimes(3));

    expect(confirm).toHaveBeenCalledOnce();
    expect(request).toHaveBeenLastCalledWith(
      '/api/v1/code-generation/runs/apply',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({
          previewRunId: '0198f36e-f7a7-7c52-9cbb-774e67411212'
        })
      })
    );
    controller.dispose();
  });
});
