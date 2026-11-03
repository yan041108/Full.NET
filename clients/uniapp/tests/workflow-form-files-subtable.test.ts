import { describe, expect, it, vi } from 'vitest';
import { createWorkflowFormModel } from '../src/features/workflow/workflow-form-model';
import { createWorkflowTodoClient } from '../src/features/workflow/workflow-todo-client';

const id = '01912345-6789-7abc-8def-0123456789ab';
const schema = { schemaVersion: 1, adapterVersion: 1, sections: [{ sectionKey: 'main', fields: [
  { fieldKey: 'files', fieldTypeKey: 'attachment', required: false, constraints: { maxCount: 2, maxSizeBytes: 1024, allowedExtensions: ['.pdf'] } },
  { fieldKey: 'rows', fieldTypeKey: 'subtable', required: false, constraints: { maxRows: 2, columns: [
    { columnKey: 'title', fieldTypeKey: 'text', required: true, constraints: { maxLength: 20 } },
    { columnKey: 'count', fieldTypeKey: 'integer', required: false, constraints: {} },
    { columnKey: 'tags', fieldTypeKey: 'checkbox', required: false, constraints: { options: ['one', 'two'] } }
  ] } }
] }] };

describe('移动附件与明细表单边界', () => {
  it('附件只提交有界唯一 UUID，禁止 URL、伪文件及超量', () => {
    const model = createWorkflowFormModel(schema, {});
    expect(model.updateValue('files', [id])).toBe(true);
    expect(model.updateValue('files', ['https://example.test/file'])).toBe(false);
    expect(model.updateValue('files', [{ fileId: id }])).toBe(false);
    expect(model.updateValue('files', [id, id])).toBe(false);
    expect(model.state.patch.files).toEqual([id]);
  });
  it('明细归一化标量，拒绝未知列、非法类型及超行数', () => {
    const model = createWorkflowFormModel(schema, {});
    expect(model.updateValue('rows', [{ title: 'item', count: '2' }])).toBe(true);
    expect(model.state.patch.rows).toEqual([{ title: 'item', count: 2 }]);
    for (const value of [[{ unknown: 'x' }], [{ count: 'bad' }], [{}, {}, {}], ['not-row']]) {
      expect(model.updateValue('rows', value)).toBe(false);
    }
  });
  it('行必填参与验证，隐藏和只读字段不能修改', () => {
    const model = createWorkflowFormModel(schema, { rows: [{}] }, { files: 'readOnly' });
    expect(model.validate()).toEqual({ rows: 'required' });
    expect(model.updateValue('files', [id])).toBe(false);
    expect(model.updateValue('rows', [{ title: 'ready' }])).toBe(true);
    expect(model.validate()).toEqual({});
    const hidden = createWorkflowFormModel(schema, { rows: [{}] }, { rows: 'hidden' });
    expect(hidden.validate()).toEqual({});
    expect(hidden.updateValue('rows', [])).toBe(false);
  });
  it('输入、公开状态和补丁的明细嵌套值不共享可变引用', () => {
    const input = { rows: [{ title: 'item', tags: ['one'] }] };
    const model = createWorkflowFormModel(schema, input);
    input.rows[0]!.tags.push('two');
    const values = model.state.values.rows as { title: string; tags: string[] }[];
    values[0]!.title = 'mutated'; values[0]!.tags.push('two');
    expect(model.state.values.rows).toEqual([{ title: 'item', tags: ['one'] }]);
    expect(model.state.patch).toEqual({});
  });
  it('上传固定到 Todo、修订和主表/选票上下文并运行时读取响应', async () => {
    const response = { fileId: id, contentHash: null, contentType: 'application/pdf', originalFileName: 'proof.pdf', sizeBytes: 128 };
    const http = { request: vi.fn(), upload: vi.fn().mockResolvedValue(response) };
    const client = createWorkflowTodoClient(http, { read: vi.fn(), write: vi.fn() });
    expect(await client.uploadAttachment(id, 3, 'files', 'vote', '/local/proof.pdf')).toEqual(response);
    expect(http.upload).toHaveBeenCalledWith({ path: `/api/v1/workflow/todos/${id}/attachment-uploads?expectedRevision=3&fieldKey=files&formKindKey=vote`, filePath: '/local/proof.pdf' });
    await expect(client.uploadAttachment(id, -1, 'files', 'main', '/path')).rejects.toThrow();
    await expect(client.uploadAttachment(id, 1, '../file', 'main', '/path')).rejects.toThrow();
    http.upload.mockResolvedValue({ fileId: 'https://example.test/file' });
    await expect(client.uploadAttachment(id, 1, 'files', 'main', '/path')).rejects.toThrow();
    expect(http.upload).toHaveBeenCalledTimes(2);
  });
});
