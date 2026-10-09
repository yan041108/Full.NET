import type { SupportedLocale } from '@fullnet/admin-i18n';

// 审核界面仅翻译固定机器码，不把反馈正文或主体标识插入文案。
const zh = {
  title: '反馈审核', queue: '审核队列', labels: '标签统计', refresh: '刷新队列', previous: '上一页', next: '下一页', close: '关闭',
  approve: '批准', reject: '驳回', approved: '已批准', rejected: '已驳回', pending_review: '待审核',
  rating: '评价', reason: '原因', citations: '引用数', state: '审核状态', versions: '反馈 / 审核版本', actions: '操作', count: '反馈数',
  helpful: '有帮助', unhelpful: '没有帮助', accurate: '准确', useful: '实用', incomplete: '不完整', unsupported: '缺少依据', outdated: '已过时', other: '其他',
  hint: '这里只审核结构化标签，不提供原始问答、用户或引用内容。批准不会自动进入评估集或影响模型。',
  privacy: '统计仅覆盖最多 64 项当前批准反馈，每格至少五名不同提交者。精确计数存在差分风险，小样本抑制不保证匿名化。',
  preview: '读取统计', download: '下载标签 JSON', empty: '本扫描页没有可审核条目；有下一页时可继续浏览。', emptyStats: '当前窗口没有达到提交者阈值的标签。',
  partial: '有界统计 · 不代表总体', notLoaded: '尚未读取统计', confirm: '确认审核当前标签及版本？', updated: '审核已更新',
  retry: '审核结果未知或版本已变化，请刷新队列后再审核。', unavailable: '当前无法读取反馈审核数据。', page: '扫描页'
};
const en: Record<keyof typeof zh, string> = {
  title: 'Feedback review', queue: 'Review queue', labels: 'Label statistics', refresh: 'Refresh queue', previous: 'Previous', next: 'Next', close: 'Close',
  approve: 'Approve', reject: 'Reject', approved: 'Approved', rejected: 'Rejected', pending_review: 'Pending review',
  rating: 'Rating', reason: 'Reason', citations: 'Citations', state: 'Review state', versions: 'Feedback / review versions', actions: 'Actions', count: 'Feedback count',
  helpful: 'Helpful', unhelpful: 'Unhelpful', accurate: 'Accurate', useful: 'Useful', incomplete: 'Incomplete', unsupported: 'Unsupported', outdated: 'Outdated', other: 'Other',
  hint: 'Review structured labels only. Original answers, people and citation contents are unavailable. Approval does not automatically admit feedback into evaluation or affect the model.',
  privacy: 'Statistics cover at most 64 currently approved items, with five distinct submitters per cell. Exact counts permit differencing; small-sample suppression does not guarantee anonymity.',
  preview: 'Read statistics', download: 'Download label JSON', empty: 'No eligible items on this scan page. Continue if another page is available.', emptyStats: 'No labels in this window meet the submitter threshold.',
  partial: 'Bounded statistics · not the population', notLoaded: 'Statistics not loaded', confirm: 'Confirm this label and its current versions?', updated: 'Review updated',
  retry: 'The review outcome is unknown or its version changed. Refresh the queue before reviewing again.', unavailable: 'Feedback review data is currently unavailable.', page: 'Scan page'
};
export type FeedbackReviewTextKey = keyof typeof zh;
export const feedbackReviewText = (locale: SupportedLocale, key: FeedbackReviewTextKey): string => (locale === 'en-US' ? en : zh)[key];
