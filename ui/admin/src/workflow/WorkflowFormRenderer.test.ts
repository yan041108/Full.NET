import { flushPromises, mount } from '@vue/test-utils';
import { describe, expect, it, vi } from 'vitest';
import type { WorkflowFormSchema } from '@fullnet/client-contracts';
import WorkflowFormRenderer from './WorkflowFormRenderer.vue';
import { downloadWorkflowTodoFormAttachment, openWorkflowFormAttachmentBlob, uploadWorkflowFormAttachment } from './workflow-form-attachments';
vi.mock('./workflow-form-attachments', () => ({ downloadWorkflowFormAttachment: vi.fn(), downloadWorkflowTodoFormAttachment: vi.fn(), openWorkflowFormAttachmentBlob: vi.fn(), uploadWorkflowFormAttachment: vi.fn() }));

const schema = {
  schemaVersion: 1,
  adapterVersion: 1,
  sections: [{
    sectionKey: 'request',
    fields: [
      { fieldKey: 'summary', fieldTypeKey: 'text', required: true, constraints: {} },
      { fieldKey: 'amount', fieldTypeKey: 'integer', required: false, constraints: {} },
      { fieldKey: 'secret', fieldTypeKey: 'text', required: false, constraints: {} }
    ]
  }]
} satisfies WorkflowFormSchema;

describe('WorkflowFormRenderer', () => {
  it('共享附件初始名称只作为文本和下载名，隐藏字段不显示名称，切换资源不保留旧名称', async () => {
    vi.mocked(downloadWorkflowTodoFormAttachment).mockResolvedValueOnce(new Blob(['bytes']));
    const files = { schemaVersion: 1, adapterVersion: 1, sections: [{ sectionKey: 'main', fields: ['input', 'secret'].map(fieldKey => ({
      fieldKey, fieldTypeKey: 'attachment', required: false, constraints: { maxCount: 1, maxSizeBytes: 1024, allowedExtensions: ['png'] } })) }] } satisfies WorkflowFormSchema;
    const wrapper = mount(WorkflowFormRenderer, { props: { schema: files, todoId: 'first', submission: { input: ['opaque'], secret: ['private'] },
      fieldPolicies: { input: 'readOnly', secret: 'hidden' }, attachmentNames: { opaque: '<img src=x>.png', private: 'private-name.png' } } });
    try {
      expect(wrapper.text()).toContain('<img src=x>.png'); expect(wrapper.find('img').exists()).toBe(false);
      expect(wrapper.text()).not.toContain('private-name.png');
      await wrapper.get('.workflow-form__attachment-link').trigger('click'); await flushPromises();
      expect(openWorkflowFormAttachmentBlob).toHaveBeenCalledWith(expect.any(Blob), '<img src=x>.png');
      await wrapper.setProps({ todoId: 'second', submission: { input: ['next'] }, attachmentNames: { next: '新资料.png' } });
      expect(wrapper.text()).toContain('新资料.png'); expect(wrapper.text()).not.toContain('<img src=x>.png');
    } finally { wrapper.unmount(); }
  });
  it('复用当前可见输入的不透明引用，不要求上传权限，不暴露隐藏字段且遵守数量上限', async () => {
    const filesSchema = { schemaVersion: 1, adapterVersion: 1, sections: [{ sectionKey: 'main', fields:
      ['input', 'private', 'output'].map(fieldKey => ({ fieldKey, fieldTypeKey: 'attachment', required: false,
        constraints: { maxCount: 1, maxSizeBytes: 1024, allowedExtensions: ['png'] } })) }] } satisfies WorkflowFormSchema;
    const wrapper = mount(WorkflowFormRenderer, { props: { schema: filesSchema, todoId: 'task',
      submission: { input: ['opaque-capability'], private: ['private-capability'] },
      fieldPolicies: { input: 'readOnly', private: 'hidden', output: 'editable' } } });
    try {
      expect(wrapper.find('input[type=file]').exists()).toBe(false);
      const output = wrapper.get('[data-testid="workflow-form-attachment-output"]');
      expect(output.element.closest('.workflow-form__field')?.tagName).toBe('DIV');
      expect(output.element.closest('[role="group"]')?.getAttribute('aria-label')).toBe('output');
      expect(output.findAll('[data-reuse-file-id]')).toHaveLength(1);
      expect(wrapper.html()).not.toContain('private-capability');
      await output.get('[data-reuse-file-id="opaque-capability"]').trigger('click');
      expect(wrapper.emitted('update:patch')?.at(-1)).toEqual([{ output: ['opaque-capability'] }]);
      expect(output.findAll('[data-reuse-file-id]')).toHaveLength(0);
      await wrapper.setProps({ fieldPolicies: { input: 'hidden', private: 'hidden', output: 'readOnly' } });
      expect(wrapper.find('[data-reuse-file-id]').exists()).toBe(false);
      await wrapper.setProps({ todoId: 'new-task', submission: { input: ['new-capability'] },
        fieldPolicies: { input: 'readOnly', private: 'hidden', output: 'editable' } });
      expect(wrapper.html()).not.toContain('opaque-capability');
      expect(wrapper.find('[data-reuse-file-id="new-capability"]').exists()).toBe(true);
    } finally { wrapper.unmount(); }
  });
  it('上传需要显式权限与资源，切换详情后丢弃旧上传回执', async () => {
    let resolve!: (value: Awaited<ReturnType<typeof uploadWorkflowFormAttachment>>) => void;
    vi.mocked(uploadWorkflowFormAttachment).mockImplementationOnce(() => new Promise(done => { resolve = done; }));
    const wrapper = mount(WorkflowFormRenderer, { props: { schema: { schemaVersion: 1, adapterVersion: 1, sections: [{ sectionKey: 'main', fields: [{
      fieldKey: 'file', fieldTypeKey: 'attachment', required: false, constraints: { maxCount: 1, maxSizeBytes: 1024, allowedExtensions: ['png'] } }] }] },
      submission: {}, fieldPolicies: { file: 'editable' } } });
    try {
      expect(wrapper.find('input[type=file]').exists()).toBe(false);
      await wrapper.setProps({ canUploadAttachments: true, uploadContext: { kind: 'todo', todoId: 'old', expectedRevision: 1, formKindKey: 'main' } });
      const file = new File(['image'], 'proof.png'); const input = wrapper.get('input[type=file]');
      Object.defineProperty(input.element, 'files', { value: [file], configurable: true }); await input.trigger('change');
      const call = vi.mocked(uploadWorkflowFormAttachment).mock.calls.at(-1)!;
      expect(call[1]).toBe('file'); expect(call[2]).toEqual({ kind: 'todo', todoId: 'old', expectedRevision: 1, formKindKey: 'main' });
      await wrapper.setProps({ uploadContext: { kind: 'todo', todoId: 'new', expectedRevision: 2, formKindKey: 'main' } });
      expect(call[3]?.aborted).toBe(true);
      resolve({ fileId: 'old-file', originalFileName: 'proof.png', contentType: 'image/png', sizeBytes: 5, contentHash: null }); await flushPromises();
      expect(wrapper.emitted('update:patch')?.some(event => JSON.stringify(event).includes('old-file'))).toBe(false);
    } finally { wrapper.unmount(); }
  });
  it('附件使用本人Todo，切换详情后忽略迟到的旧文件', async () => {
    let resolve!: (blob: Blob) => void; vi.mocked(downloadWorkflowTodoFormAttachment).mockImplementationOnce(() => new Promise(done => { resolve = done; }));
    const wrapper = mount(WorkflowFormRenderer, { props: { schema: { schemaVersion: 1, adapterVersion: 1, sections: [{ sectionKey: 'main', fields: [{
      fieldKey: 'file', fieldTypeKey: 'attachment', required: false, constraints: { maxCount: 1, maxSizeBytes: 1024, allowedExtensions: ['png'] } }] }] },
      submission: { file: ['file-id'] }, fieldPolicies: { file: 'readOnly' }, instanceId: 'instance-id', todoId: 'todo-id' } });
    try {
      await wrapper.get('.workflow-form__attachment-link').trigger('click');
      expect(downloadWorkflowTodoFormAttachment).toHaveBeenCalledWith('todo-id', 'file-id', expect.any(AbortSignal));
      await wrapper.setProps({ todoId: 'new-todo' }); resolve(new Blob(['private'])); await flushPromises();
      expect(openWorkflowFormAttachmentBlob).not.toHaveBeenCalled();
    } finally { wrapper.unmount(); }
  });
  it('隐藏不可见字段、锁定只读字段并只发送可编辑 patch', async () => {
    const wrapper = mount(WorkflowFormRenderer, {
      props: {
        schema,
        submission: { summary: '原始摘要', amount: 12 },
        fieldPolicies: {
          summary: 'readOnly',
          amount: 'required',
          secret: 'hidden'
        }
      }
    });

    expect(wrapper.find('[data-field-key="secret"]').exists()).toBe(false);
    expect(wrapper.find('[data-field-key="summary"] input').attributes('readonly')).toBeDefined();

    await wrapper.find('[data-field-key="amount"] input').setValue('25');

    expect(wrapper.emitted('update:patch')?.at(-1)).toEqual([{ amount: 25 }]);
  });

  it('把发布版本的文本长度和整数范围投影为原生输入约束', () => {
    const constrainedSchema = {
      ...schema,
      sections: [{
        sectionKey: 'request',
        fields: [
          {
            fieldKey: 'summary',
            fieldTypeKey: 'text',
            required: true,
            constraints: { minLength: 2, maxLength: 64 }
          },
          {
            fieldKey: 'amount',
            fieldTypeKey: 'integer',
            required: false,
            constraints: { minimum: 1, maximum: 100 }
          }
        ]
      }]
    } satisfies WorkflowFormSchema;

    const wrapper = mount(WorkflowFormRenderer, {
      props: {
        schema: constrainedSchema,
        submission: {},
        fieldPolicies: {}
      }
    });

    expect(wrapper.get('[data-field-key="summary"] input').attributes()).toMatchObject({
      minlength: '2',
      maxlength: '64'
    });
    expect(wrapper.get('[data-field-key="amount"] input').attributes()).toMatchObject({
      min: '1',
      max: '100'
    });
  });

  it('在 datetime-local 与带时区的权威日期时间之间转换', async () => {
    const dateTimeSchema = {
      schemaVersion: 1,
      adapterVersion: 1,
      sections: [{
        sectionKey: 'request',
        fields: [{
          fieldKey: 'deadline',
          fieldTypeKey: 'datetime',
          required: true,
          constraints: {}
        }]
      }]
    } satisfies WorkflowFormSchema;
    const authoritativeValue = '2026-08-30T02:30:15Z';
    const wrapper = mount(WorkflowFormRenderer, {
      props: {
        schema: dateTimeSchema,
        submission: { deadline: authoritativeValue },
        fieldPolicies: {}
      }
    });
    const input = wrapper.get('[data-field-key="deadline"] input');

    expect((input.element as HTMLInputElement).value.slice(0, 19))
      .toBe(toLocalDateTime(authoritativeValue));
    expect(input.attributes('step')).toBe('1');

    const localValue = '2026-08-30T10:45:30';
    await input.setValue(localValue);
    const emitted = wrapper.emitted('update:patch')?.at(-1)?.[0] as Record<string, unknown>;

    expect(emitted.deadline).toMatch(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$/);
    expect(Date.parse(String(emitted.deadline))).toBe(new Date(localValue).getTime());
  });
});

function toLocalDateTime(value: string): string {
  const date = new Date(value);
  const pad = (part: number) => String(part).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
    + `T${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`;
}
