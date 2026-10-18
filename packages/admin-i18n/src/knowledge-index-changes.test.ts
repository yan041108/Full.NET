import { expect, it } from 'vitest';
import { translateKnowledgeIndexChanges } from './knowledge-index-changes.js';

it('来源目录两种语言明确区分来源确认与索引就绪', () => {
  expect(translateKnowledgeIndexChanges('zh-CN', 'title')).toBe('已确认来源');
  expect(translateKnowledgeIndexChanges('en-US', 'title')).toBe('Confirmed sources');
  expect(translateKnowledgeIndexChanges('zh-CN', 'hint')).toContain('不表示索引已就绪');
  expect(translateKnowledgeIndexChanges('en-US', 'hint')).toContain('explicit confirmation');
});
