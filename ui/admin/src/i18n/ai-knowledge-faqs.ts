const zh = { businessSources:'不可变本人日程来源',scheduleId:'本人日程 UUID',captureSchedule:'核对并附加本人日程',previewSchedule:'预览本人日程',businessHint:'日程来源仅本人可读。独立审核者仍须具备每项来源的当前读取资格；人工脱敏不会扩大权限。', feedbackDerived:'批准反馈派生', feedbackSource:'原始反馈及审核版本（不可变）', parentDerived:'FAQ 派生', parents:'父 FAQ 当前发布与关系',parentEdges:'上游父节点',none:'无上游父节点',
  import:'导入 FAQ 包',export:'导出 FAQ 包',
  title: 'FAQ 维护', hint: '草稿、索引和发布分别管理。文档派生来源由服务器冻结，编辑、复制和恢复不会清空；来源撤权或发布替换后拒绝读取。', govern:'索引与审核发布',
  create: '独立编写草稿', fromDocuments: '创建 FAQ 草稿', target: '保存到自己管理的知识库', question: '标准问题', aliases: '相似问法（每行一个，最多十六个）',
  answer: '答案', category: '分类', basis: '独立编写依据（保存后不可更改）', validFrom: '未来发布生效时间（可选）', expires: '未来发布截止时间（可选）',
  independent: '独立编写', derived: '文档派生', sources: '不可变原始来源', save: '保存草稿', saved: '草稿已保存，尚未发布', history: '读取修订历史', restore: '将此正文恢复为新修订', copy: '复制并保留原始来源',
  revision: '修订', preview: '预览原始文件', reload: '明确读取当前头', conflict: '草稿头已变化。未保存输入保留；请核对当前头后再编辑。',
  failed: '操作未确认，未自动重试。可以用原请求重试；编辑输入将开始新请求。', unavailable: '当前草稿、完整来源或权限不可用，已清空旧正文。',
  invalid: '请填写问题、答案、分类；独立编写必须填写依据。', next: '下一页', refresh: '重新读取', empty: '当前没有可读草稿', loading: '正在核对来源',
  draft: '草稿 · 尚未发布', close: '关闭', load: '打开草稿', replayed: '原请求已保存，显示当前头', readOnly: '当前仅允许读取', ready: '请选择自己管理的知识库。'
};
const en: Record<keyof typeof zh, string> = { businessSources:'Immutable personal schedule sources',scheduleId:'Personal schedule UUID',captureSchedule:'Verify and attach personal schedule',previewSchedule:'Preview personal schedule',businessHint:'Schedules remain owner-only. Independent reviewers need current access to every source; redaction does not expand permissions.', feedbackDerived:'Derived from approved feedback', feedbackSource:'Immutable feedback and review versions', parentDerived:'Derived from FAQs', parents:'Current parent FAQ publications',parentEdges:'Upstream parents',none:'No upstream parent',
  import:'Import FAQ package',export:'Export FAQ package',
  title: 'FAQ maintenance', hint: 'Draft, index and publication are separate states. Server-frozen document provenance survives edits, copies and restores. Revoked or replaced sources block access.', govern:'Index and publication review',
  create: 'Write an independent draft', fromDocuments: 'Create FAQ draft', target: 'Save in a library you manage', question: 'Canonical question', aliases: 'Alternative questions (one per line, at most sixteen)',
  answer: 'Answer', category: 'Category', basis: 'Independent basis (immutable after saving)', validFrom: 'Future publication starts (optional)', expires: 'Future publication ends (optional)',
  independent: 'Independently authored', derived: 'Derived from documents', sources: 'Immutable original sources', save: 'Save draft', saved: 'Draft saved; not published', history: 'Read revision history', restore: 'Restore this content as a new revision', copy: 'Copy with original provenance',
  revision: 'Revision', preview: 'Preview original file', reload: 'Explicitly read current head', conflict: 'The draft head changed. Unsaved input is retained. Review the current head before editing again.',
  failed: 'Operation unconfirmed; no automatic retry. Retry with the original request or edit the input to start a new request.', unavailable: 'Draft, complete sources or permissions unavailable. Old content cleared.',
  invalid: 'Enter a question, answer and category. Independent authoring requires a basis.', next: 'Next page', refresh: 'Read again', empty: 'No readable drafts', loading: 'Verifying sources',
  draft: 'Draft · unpublished', close: 'Close', load: 'Open draft', replayed: 'Original request saved; displaying current head', readOnly: 'Read access only', ready: 'Select a library you manage.'
};
export const knowledgeFaqText = (locale: string, key: keyof typeof zh) => (locale.startsWith('zh') ? zh : en)[key];
