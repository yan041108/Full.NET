import { describe, expect, it } from 'vitest';
import workflowFixture from '../../../packages/client-contracts/src/fixtures/workflow-form-schema-v1.json';
import { createWorkflowFormModel } from '../src/features/workflow/workflow-form-model';

describe('workflow form model', () => {
  it('reads the shared Golden Schema and applies hidden, read-only and required policies', () => {
    const model = createWorkflowFormModel(
      workflowFixture.formSchema,
      workflowFixture.submission,
      workflowFixture.fieldPolicies
    );

    expect(model.state.sections.flatMap(section => section.fields.map(field => field.fieldKey)))
      .toEqual([
        'summary',
        'quantity',
        'rate',
        'amount',
        'dueDate',
        'cutoff',
        'deadline',
        'priority',
        'reviewers',
        'category',
        'notify'
      ]);
    expect(model.state.fieldsByKey.quantity?.readOnly).toBe(true);
    expect(model.state.fieldsByKey.summary?.required).toBe(true);
    expect(model.state.values.amount).toBe('1288.50');
    expect(model.state.values.quantity).toBe(10);
    expect(model.state.values.deadline).toBe('2026-09-01T09:30:00+08:00');
  });

  it('emits only editable changed values and keeps decimal values as strings', () => {
    const model = createWorkflowFormModel(
      workflowFixture.formSchema,
      workflowFixture.submission,
      workflowFixture.fieldPolicies
    );

    expect(model.updateValue('notes', 'cannot reveal hidden fields')).toBe(false);
    expect(model.updateValue('quantity', 99)).toBe(false);
    expect(model.updateValue('unknown', 'ignored')).toBe(false);
    expect(model.updateValue('amount', ' 1300.50 ')).toBe(true);
    expect(model.updateValue('reviewers', ['finance', 'finance', 'owner', 'unknown'])).toBe(true);

    expect(model.state.patch).toEqual({
      amount: '1300.50',
      reviewers: ['finance', 'owner']
    });
    expect(model.state.values.amount).toBe('1300.50');
  });

  it('validates required fields without adding unchanged values to the patch', () => {
    const model = createWorkflowFormModel(
      workflowFixture.formSchema,
      workflowFixture.submission,
      workflowFixture.fieldPolicies
    );

    expect(model.updateValue('summary', '   ')).toBe(true);
    expect(model.validate()).toEqual({ summary: 'required' });
    expect(model.updateValue('summary', '采购审批')).toBe(true);
    expect(model.state.patch).toEqual({});
    expect(model.validate()).toEqual({});
  });

  it('fails closed for an unknown schema version or unknown policy key', () => {
    expect(() => createWorkflowFormModel(
      { ...workflowFixture.formSchema, schemaVersion: 2 },
      workflowFixture.submission,
      workflowFixture.fieldPolicies
    )).toThrowError('workflow.form.unsupported-schema');

    const unknownFieldSchema = structuredClone(workflowFixture.formSchema);
    unknownFieldSchema.sections[0]!.fields[0]!.fieldTypeKey = 'html';
    expect(() => createWorkflowFormModel(
      unknownFieldSchema,
      workflowFixture.submission,
      workflowFixture.fieldPolicies
    )).toThrowError('workflow.form.unsupported-schema');

    expect(() => createWorkflowFormModel(
      workflowFixture.formSchema,
      workflowFixture.submission,
      { ...workflowFixture.fieldPolicies, injected: 'editable' }
    )).toThrowError('workflow.form.invalid-policies');
  });
});
