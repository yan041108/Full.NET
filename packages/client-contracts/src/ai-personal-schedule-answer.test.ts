import { describe, expect, it } from 'vitest';
import { isAiChatMessage, isAskAiKnowledgeResponse } from './ai-chat.js';
const id = (index: number) => `01900000-0000-7000-8000-${String(index).padStart(12, '0')}`;
const citation = () => ({ id: id(3), kind: 'calendar-personal-schedule-v1', purpose: 'knowledge-assistant-v1', scheduleId: id(4), version: 2, projectionHash: 'A'.repeat(64) });
const answer = () => ({ refused: false, message: { id: id(1), sessionId: id(2), roleKey: 'assistant', content: '日程与知识答案', statusKey: 'completed',
  promptTokens: null, completionTokens: null, createdAtUtc: '2026-10-31T00:00:00Z', citations: [], personalScheduleCitations: [citation()] } });
describe('独立日程引用与混合知识答案', () => {
  it('接受仅引用日程的混合答案，不要求伪造文档引用', () => { expect(isAskAiKnowledgeResponse(answer())).toBe(true); });
  it.each(['kind', 'purpose', 'scheduleId', 'version', 'projectionHash', 'documentId', 'content'])('拒绝未知或伪装字段 %s', field => {
    const value = answer(); Object.assign(value.message.personalScheduleCitations[0]!, { [field]: field === 'version' ? 0 : 'invalid' });
    expect(isAskAiKnowledgeResponse(value)).toBe(false); expect(isAiChatMessage(value.message)).toBe(false);
  });
  it('跨来源引用身份不能重复，并以同一上限核对所有引用', () => {
    const value = answer(); value.message.personalScheduleCitations.push(citation()); expect(isAskAiKnowledgeResponse(value)).toBe(false);
    value.message.personalScheduleCitations = Array.from({ length: 8 }, (_, index) => ({ ...citation(), id: id(10 + index) }));
    expect(isAskAiKnowledgeResponse(value)).toBe(false);
  });
  it('拒答不允许夹带日程引用，空引用拒答保持可读', () => {
    const value = answer(); value.refused = true; value.message.content = ''; expect(isAskAiKnowledgeResponse(value)).toBe(false);
    value.message.personalScheduleCitations = []; expect(isAskAiKnowledgeResponse(value)).toBe(true);
  });
});
