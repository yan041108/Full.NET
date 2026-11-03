const zh = {
  title: '知识组织与发布', folder: '文件夹', root: '根目录', name: '文件夹名称', parent: '父文件夹', createFolder: '创建文件夹',
  saveFolder: '保存文件夹', deleteFolder: '删除空文件夹', renameFolder: '维护文件夹', tag: '标签', tags: '标签（逗号分隔，最多 10 个）', responsible: '负责人 ID',
  filter: '筛选', refresh: '刷新', all: '全部', state: '发布状态', draft: '草稿／已下架', published: '已发布', scheduled: '待生效', expired: '已到期',
  organization: '组织信息', apply: '应用到所选文档', retry: '只重试失败项', success: '成功', failed: '失败或未确认', noSelection: '请先选择文档，单批最多 20 项。',
  organizationHint: '文件夹、标签和负责人仅组织内容，不会授予读取或发布权限。', publicationHint: '索引完成只是候选。新草稿不覆盖有效发布版；下架或到期后不回退旧版。',
  publication: '发布治理', candidate: '候选文件版本', proposalId: '提案 ID', readProposal: '读取提案', request: '请求独立审核',
  from: '生效时间', to: '失效时间', approve: '批准提案', reject: '拒绝提案', publish: '确认正式发布', withdraw: '撤回当前发布',
  hash: '冻结提案摘要', policy: '审核策略版本', requester: '原维护主体', reviewer: '审核主体', head: '发布头版本', version: '并发版本',
  pending: '待独立审核', approved: '已批准，尚未发布', rejected: '已拒绝', superseded: '已被新发布替换', withdrawn: '已下架',
  preview: '预览固定来源', confirmRequest: '确认已核对候选来源及有效期，并提交独立人工审核？',
  confirmReview: '确认已核对固定来源、摘要及有效期，并作出当前决定？原维护主体不能自行批准。',
  confirmPublish: '确认将已批准的原候选切换为正式内容？原维护会话、候选及发布头版本将重新校验。',
  confirmWithdraw: '确认撤回当前发布？后续访问将停止，不会自动回退旧版。', confirmDelete: '确认删除此空文件夹？',
  unavailable: '操作未完成，请刷新权威状态后重试。', unknown: '决定结果未确认。请先刷新提案和发布头，勿重复派发。',
  invalid: '请检查标识、标签数量和有效期。', lookup: '读取', close: '关闭', empty: '当前范围没有文档。'
};
const en: Record<keyof typeof zh, string> = {
  title: 'Knowledge organization and publication', folder: 'Folder', root: 'Root', name: 'Folder name', parent: 'Parent folder', createFolder: 'Create folder',
  saveFolder: 'Save folder', deleteFolder: 'Delete empty folder', renameFolder: 'Manage folder', tag: 'Tag', tags: 'Tags (comma separated, up to 10)', responsible: 'Responsible user ID',
  filter: 'Filter', refresh: 'Refresh', all: 'All', state: 'Publication state', draft: 'Draft / withdrawn', published: 'Published', scheduled: 'Scheduled', expired: 'Expired',
  organization: 'Organization', apply: 'Apply to selected documents', retry: 'Retry failed items only', success: 'Succeeded', failed: 'Failed or unconfirmed', noSelection: 'Select up to 20 documents.',
  organizationHint: 'Folders, tags and responsibility do not grant read or publish permissions.', publicationHint: 'An indexed generation is only a candidate. New drafts preserve valid publications. Withdrawal or expiry never restores an older release.',
  publication: 'Publication governance', candidate: 'Candidate file version', proposalId: 'Proposal ID', readProposal: 'Read proposal', request: 'Request independent review',
  from: 'Effective from', to: 'Expires at', approve: 'Approve proposal', reject: 'Reject proposal', publish: 'Publish approved candidate', withdraw: 'Withdraw current publication',
  hash: 'Frozen proposal digest', policy: 'Review policy version', requester: 'Original maintainer', reviewer: 'Reviewer', head: 'Publication head version', version: 'Concurrency version',
  pending: 'Awaiting independent review', approved: 'Approved, not published', rejected: 'Rejected', superseded: 'Superseded', withdrawn: 'Withdrawn',
  preview: 'Preview fixed source', confirmRequest: 'Confirm the candidate source and validity period, then request independent human review?',
  confirmReview: 'Confirm the fixed source, digest and validity period before deciding? The original requester cannot self approve.',
  confirmPublish: 'Publish this approved candidate? The original maintainer session, candidate and publication head will be checked again.',
  confirmWithdraw: 'Withdraw this publication? Further access will stop without reverting to an older release.', confirmDelete: 'Delete this empty folder?',
  unavailable: 'The operation did not complete. Refresh authoritative state before retrying.', unknown: 'Decision outcome is unconfirmed. Refresh the proposal and publication head before retrying.',
  invalid: 'Check identifiers, tag limits and the validity period.', lookup: 'Read', close: 'Close', empty: 'No documents in this scope.'
};
export const knowledgeGovernanceText = (locale: string, key: keyof typeof zh) => (locale.startsWith('en') ? en : zh)[key];
