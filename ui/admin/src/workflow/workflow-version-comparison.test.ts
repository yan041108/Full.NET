import { describe, expect, it } from 'vitest';
import { compareWorkflowVersions, formatWorkflowVersionValue } from './workflow-version-comparison';
import { comparisonDraft, comparisonForm, comparisonIds as ids, comparisonVersion } from './workflow-version-comparison.fixtures';

describe('固定版本结构对比', () => {
  it('忽略对象键与节点存放顺序，不把发布时间或版本编号当成执行差异', () => {
    const draft = comparisonDraft(); draft.nodes.reverse();
    draft.nodes.forEach(node => { node.config = Object.fromEntries(Object.entries(node.config).reverse()); });
    const result = compareWorkflowVersions(comparisonVersion(ids.before), comparisonVersion(ids.after, JSON.stringify(draft)), comparisonForm(), comparisonForm());
    expect(result.totalChanges).toBe(0);
  });
  it('单列字段约束、字段授权、固定表单及子流程/协作引用变化', () => {
    const draft = comparisonDraft();
    draft.nodes[1]!.config.fieldPolicies = { title: 'readOnly' };
    draft.nodes.push({ nodeKey: 'child', nodeTypeKey: 'process.subprocess', nodeSchemaVersion: 1,
      config: { callPolicy: { definitionVersionId: ids.before } } });
    draft.nodes.push({ nodeKey: 'remote', nodeTypeKey: 'process.collaboration', nodeSchemaVersion: 1,
      config: { collaborationPolicy: { grantId: ids.before, grantRevision: 2, policyHash: 'a'.repeat(64) } } });
    const before = comparisonVersion(ids.before, JSON.stringify(draft));
    draft.nodes[3]!.config.callPolicy = { definitionVersionId: ids.after };
    draft.nodes[4]!.config.collaborationPolicy = { grantId: ids.after, grantRevision: 2, policyHash: 'b'.repeat(64) };
    draft.nodes[1]!.config.fieldPolicies = { title: 'hidden' };
    const after = { ...comparisonVersion(ids.after, JSON.stringify(draft)), formVersionId: ids.nextForm };
    const result = compareWorkflowVersions(before, after, comparisonForm(), comparisonForm(ids.nextForm, 64));
    expect(result.changes).toEqual(expect.arrayContaining([
      expect.objectContaining({ area: 'field', path: '/fields/title/constraints/maxLength', before: 128, after: 64 }),
      expect.objectContaining({ area: 'authorization', path: '/nodes/review/config/fieldPolicies/title' }),
      expect.objectContaining({ area: 'binding', path: '/formVersionId' }),
      expect.objectContaining({ area: 'binding', path: '/nodes/child/config/callPolicy/definitionVersionId' }),
      expect.objectContaining({ area: 'binding', path: '/nodes/remote/config/collaborationPolicy/grantId' })
    ]));
  });
  it('保留分支和等待顺序，报告节点新增/删除与展示快照', () => {
    const draft = comparisonDraft(); const before = comparisonVersion(ids.before, JSON.stringify(draft));
    draft.nodes[0]!.config.nextNodeKeys = ['end', 'review']; draft.nodes[1]!.awaitActivityNodeKeys = ['start']; draft.nodes.pop();
    const form = comparisonForm();
    const result = compareWorkflowVersions(before, comparisonVersion(ids.after, JSON.stringify(draft)), form,
      { ...form, version: { ...form.version, webRenderSchemaJson: '{"layout":"changed"}' } });
    expect(result.changes.some(change => change.path === '/nodes/start/config/nextNodeKeys')).toBe(true);
    expect(result.changes.some(change => change.path === '/nodes/review/awaitActivityNodeKeys')).toBe(true);
    expect(result.changes.some(change => change.path === '/nodes/end' && change.kind === 'removed')).toBe(true);
    expect(result.changes.some(change => change.path === '/form/webRenderSnapshot')).toBe(true);
  });
  it('拒绝错定义、错固定表单、重复节点及无界内容', () => {
    const before = comparisonVersion(ids.before), after = comparisonVersion(ids.after), form = comparisonForm();
    expect(() => compareWorkflowVersions(before, { ...after, definitionId: ids.after }, form, form)).toThrow();
    expect(() => compareWorkflowVersions(before, after, form, comparisonForm(ids.nextForm))).toThrow();
    const draft = comparisonDraft(); draft.nodes.push(draft.nodes[0]!);
    expect(() => compareWorkflowVersions(before, comparisonVersion(ids.after, JSON.stringify(draft)), form, form)).toThrow();
    expect(() => compareWorkflowVersions(before, { ...after, canonicalJson: ' '.repeat(4 * 1024 * 1024 + 1) }, form, form)).toThrow();
  });
  it('展示上限不隐藏总数，值只格式化为有界文本', () => {
    const draft = comparisonDraft(); const policies = Object.fromEntries(Array.from({ length: 200 }, (_, index) => [`field${index}`, 'editable']));
    draft.nodes.forEach(node => { node.config.fieldPolicies = { ...policies }; });
    const before = comparisonVersion(ids.before, JSON.stringify(draft));
    draft.nodes.forEach(node => { node.config.fieldPolicies = Object.fromEntries(Object.keys(policies).map(key => [key, 'readOnly'])); });
    const result = compareWorkflowVersions(before, comparisonVersion(ids.after, JSON.stringify(draft)), comparisonForm(), comparisonForm());
    expect(result.totalChanges).toBe(600); expect(result.changes).toHaveLength(512); expect(result.truncated).toBe(true);
    expect(formatWorkflowVersionValue('<script>alert(1)</script>')).toContain('<script>');
    expect(formatWorkflowVersionValue('a'.repeat(4000)).length).toBeLessThanOrEqual(2001);
  });
});
