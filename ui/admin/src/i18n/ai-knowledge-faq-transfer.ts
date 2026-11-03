const zh = { parentDerived:'FAQ 派生',
  classification_conflict:'目标知识库分类低于原始来源，请选择足够分类的目标库后重新预览。',
  independent:'独立编写',derived:'文档派生',sources:'完整来源数',openDraft:'打开新草稿',file:'选择 FAQ JSON 文件（可选）',
  title:'FAQ 导入导出', export:'导出选中的 FAQ', import:'导入 FAQ 包', choose:'选择导出条目', package:'FAQ 包 JSON', preview:'核对导入包', commit:'确认逐条导入草稿', download:'下载已核证的包',
  hint:'仅接受本实例当前可读的原始修订；在当前作用域重新核证全部来源。每条独立提交，导入不会审核或发布。最多八条、512 KiB。',
  ready:'可导入', source_unavailable:'当前来源不可用或包与权威修订不符', conflict:'请求键或修订冲突', created:'已创建草稿', replayed:'原请求已保存',
  invalid:'包无效或超限，请核对后重新预览。', failed:'请求未确认，未自动重试。再次提交将复用原请求键。', unavailable:'当前权限或完整来源已失效，旧包和正文已清空。',
  completed:'已取得逐条结果；失败项未记为成功。', close:'关闭', item:'条目', loading:'正在重新核对全部来源', exported:'导出包已核证；权限或来源变化后需重新读取。'
};
const en:Record<keyof typeof zh,string> = { parentDerived:'Derived from FAQs',
  classification_conflict:'The destination classification is below the original source. Choose a sufficiently classified library and preview again.',
  independent:'Independently authored',derived:'Derived from documents',sources:'Complete source count',openDraft:'Open imported draft',file:'Choose FAQ JSON file (optional)',
  title:'FAQ import and export',export:'Export selected FAQs',import:'Import FAQ package',choose:'Select items to export',package:'FAQ package JSON',preview:'Verify package',commit:'Confirm individual draft imports',download:'Download verified package',
  hint:'Only current readable authoritative revisions from this instance are accepted. All sources are rechecked in the current scope. Each item commits independently; imports do not approve or publish. Maximum eight items and 512 KiB.',
  ready:'Ready to import',source_unavailable:'Source unavailable or package differs from its authoritative revision',conflict:'Request key or revision conflict',created:'Draft created',replayed:'Original request saved',
  invalid:'Invalid or oversized package. Review it and preview again.',failed:'Request unconfirmed; no automatic retry. Submit again with the original request keys.',unavailable:'Permissions or complete sources unavailable. Old package and content cleared.',
  completed:'Individual results received. Failed items are not counted as successful.',close:'Close',item:'Item',loading:'Rechecking complete provenance',exported:'Package verified. Read it again after permissions or sources change.'
};
export const knowledgeFaqTransferText=(locale:string,key:keyof typeof zh)=>(locale.startsWith('zh')?zh:en)[key];
