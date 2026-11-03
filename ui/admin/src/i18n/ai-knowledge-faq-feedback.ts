const zh = {
  title:'从批准反馈创建 FAQ 草稿',open:'反馈转 FAQ 草稿',hint:'只复用当前批准反馈的原回答，不读取原用户问题，不调用模型。请选择自己管理的 restricted 知识库；来源及私人读取权限持续保留。',
  target:'目标知识库（必须为 restricted）',preview:'明确预览当前原回答',question:'人工填写标准问题',aliases:'相似问法（每行一个，最多十六个）',answer:'原回答（初始答案由服务器确定）',category:'分类',
  from:'未来发布生效时间（可选）',until:'未来发布截止时间（可选）',sources:'完整不可变来源',parents:'当前父 FAQ 与上游关系',feedback:'准确反馈及审核版本',
  save:'保存为待修订草稿',retry:'用原请求重试未确认保存',saved:'草稿已保存，尚未索引、审核或发布。可打开草稿追加正文修订。',edit:'打开已保存草稿',close:'关闭',
  restricted:'目标知识库必须为 restricted，未读取原回答。',unavailable:'反馈、审核、原回答、完整来源或权限已不可用，旧正文已清空。请刷新审核队列。',
  invalid:'请先核对预览，再填写标准问题和分类。',failed:'结果未确认，未自动重试；保存请求保留原键。',loading:'正在核对当前完整来源'
};
const en:Record<keyof typeof zh,string>={
  title:'Create an FAQ draft from approved feedback',open:'Create FAQ draft',hint:'Reuse the current approved answer only, without reading the original user question or calling a model. Choose a restricted library you manage. Provenance and private-source permissions remain required.',
  target:'Target library (restricted)',preview:'Explicitly preview the current answer',question:'Write a canonical question',aliases:'Alternative questions (one per line, at most sixteen)',answer:'Original answer (initial content is determined by the server)',category:'Category',
  from:'Future publication starts (optional)',until:'Future publication ends (optional)',sources:'Complete immutable sources',parents:'Current parent FAQs and upstream relations',feedback:'Exact feedback and review versions',
  save:'Save a draft for editing',retry:'Retry the unconfirmed save with its original key',saved:'Draft saved; not indexed, reviewed or published. Open it to append content revisions.',edit:'Open saved draft',close:'Close',
  restricted:'The target must be restricted. No original answer was read.',unavailable:'Feedback, review, answer, complete sources or permissions are unavailable. Old content cleared. Refresh the review queue.',
  invalid:'Review the preview, then enter a canonical question and category.',failed:'Operation unconfirmed; no automatic retry. The original save key is retained.',loading:'Checking current complete provenance'
};
export const knowledgeFaqFeedbackText=(locale:string,key:keyof typeof zh)=>(locale.startsWith('zh')?zh:en)[key];
