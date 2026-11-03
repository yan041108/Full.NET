const zh = {
  open: '读取助手来源', title: '本人日程来源', hint: '核对本次助手可读取的日程字段。此操作不会调用模型或更改日程。',
  restricted: '仅在本人当前权限内使用', reload: '重新读取', close: '关闭',
  unavailable: '日程已变化、不可读取或权限失效，请刷新日程后重新选择。',
  version: '来源版本', hash: '字段摘要', time: '计划时间', pending: '待完成', completed: '已完成',
  ask: '结合知识库提问', question: '问题', embedding: '索引模型',
  askHint: '日程将作为附加私人上下文。选择知识库与索引模型，再选择会话并确认模型费用；不会更改日程。',
  citation: '本人日程引用', invalid: '无法读取模型或当前来源，请重新选择。', history: '在聊天页核对历史'
};
const en: Record<keyof typeof zh, string> = {
  open: 'Read assistant sources', title: 'My schedule source', hint: 'Review the schedule fields available to this assistant. This does not call a model or change the schedule.',
  restricted: 'Available only with your current access', reload: 'Read again', close: 'Close',
  unavailable: 'The schedule changed or access is unavailable. Refresh the schedule and select it again.',
  version: 'Source version', hash: 'Field digest', time: 'Planned time', pending: 'Pending', completed: 'Completed',
  ask: 'Ask with knowledge', question: 'Question', embedding: 'Index model',
  askHint: 'The schedule is additional private context. Select knowledge and an index model, then a chat session and confirm model charges. This does not change the schedule.',
  citation: 'My schedule citation', invalid: 'The model or current source is unavailable. Select again.', history: 'Check history in chat'
};
/** 日程页面来源流程使用固定中英文键，不把服务端机器码当译文。 */
export function personalScheduleAssistantText(locale: string, key: keyof typeof zh): string {
  return locale === 'en-US' ? en[key] : zh[key];
}
