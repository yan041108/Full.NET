import type { SupportedLocale } from '@fullnet/admin-i18n';
const zh = {
  title: '人工脱敏评估候选', open: '准备评估样例', question: '脱敏问题', answer: '脱敏参考答案', evidence: '脱敏评估依据',
  language: '语言', refuse: '预期拒答', prepare: '准备候选', admit: '确认准入并下载', close: '关闭',
  hint: '只填写已人工改写的内容。不要粘贴生产原文、个人信息、凭据或机密。此操作不调用模型，也不激活生产策略。候选仅在原登录内十五分钟有效，准入时重新核对审核和全部来源。',
  confirm: '我已人工核对并移除个人信息、凭据和生产机密，确认将当前摘要对应样例准入离线评估。',
  changed: '编辑内容会丢弃原候选；来源、审核或登录变化后需重新准备。', success: '来源复核通过，评估候选已下载。'
};
const en: Record<keyof typeof zh, string> = {
  title: 'Manually redacted evaluation candidate', open: 'Prepare evaluation sample', question: 'Redacted question', answer: 'Redacted reference answer', evidence: 'Redacted evaluation evidence',
  language: 'Language', refuse: 'Expect refusal', prepare: 'Prepare candidate', admit: 'Admit and download', close: 'Close',
  hint: 'Enter manually rewritten content only. Exclude production originals, personal data, credentials and secrets. This does not call a model or activate a production strategy. Candidates expire after fifteen minutes in the original login; admission rechecks the review and every source.',
  confirm: 'I have manually checked and removed personal data, credentials and production secrets. Admit the sample matching this digest to offline evaluation.',
  changed: 'Editing discards the candidate. Prepare again after a source, review or login change.', success: 'Sources rechecked; the evaluation candidate was downloaded.'
};
export const feedbackEvaluationText = (locale: SupportedLocale, key: keyof typeof zh) => (locale === 'en-US' ? en : zh)[key];
