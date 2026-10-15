import { describe, expect, it } from 'vitest';
import { translateKnowledgeProcessing } from '../src/knowledge-processing.js';

describe('knowledge processing state', () => {
  it.each(['zh-CN', 'en-US'] as const)('translates pending signal, consent and admission in %s', locale => {
    const states = ['pending_processing', 'awaiting_index_confirmation', 'index_admitted'];
    const labels = states.map(state => translateKnowledgeProcessing(locale, state));
    expect(new Set(labels).size).toBe(3);
    for (const label of labels) expect(states).not.toContain(label);
    expect(translateKnowledgeProcessing(locale, 'future_state')).toBe(translateKnowledgeProcessing(locale, ''));
  });
  it('does not describe admission as ready', () => {
    expect(translateKnowledgeProcessing('zh-CN', 'index_admitted')).toBe('索引任务已创建');
    expect(translateKnowledgeProcessing('en-US', 'awaiting_index_confirmation')).toBe('Awaiting index confirmation');
  });
});
