import {afterEach,beforeEach,describe,expect,it,vi} from 'vitest';
import {mount,flushPromises,type VueWrapper} from '@vue/test-utils';
import {createPinia,setActivePinia} from 'pinia';
import Dialog from './AiKnowledgeFaqDraftDialog.vue';
import {useSessionStore} from '../auth/session';
import * as api from '../api/ai-knowledge-faqs';
import {previewPersonalScheduleSources} from '../api/ai-personal-schedule-assistant';
vi.mock('../api/ai-personal-schedule-assistant',()=>({previewPersonalScheduleSources:vi.fn()}));
vi.mock('../api/ai-knowledge-faqs',()=>({createIndependentFaq:vi.fn(),createDerivedFaq:vi.fn(),createDocumentFaq:vi.fn(),saveFaqDraft:vi.fn(),restoreFaqDraft:vi.fn(),copyFaqDraft:vi.fn(),getFaqDraft:vi.fn(),listFaqRevisions:vi.fn()}));
const id=(n:number)=>`01900000-0000-7000-8000-${String(n).padStart(12,'0')}`;
const source={knowledgeBaseId:id(8),knowledgeBaseVersion:1,documentId:id(9),documentVersion:1,versionId:id(10),publicationId:id(11),generationId:id(12),manifestHash:'C'.repeat(64)};
const workspace=()=>({id:id(1),knowledgeBaseId:id(2),reservedDocumentId:id(3),state:'draft',origin:'document_derived',basis:null,sourceHash:'A'.repeat(64),sources:[source],headVersion:1,
  revision:{id:id(4),number:1,content:{question:'问题',aliases:[],answer:'答案',category:'制度',validFromUtc:null,expiresAtUtc:null},contentHash:'B'.repeat(64),createdAtUtc:'2026-10-29T00:00:00Z',restoredFromRevisionId:null}});
const wrappers:VueWrapper[]=[];
async function render(existing=true,allowed=true){const pinia=createPinia();setActivePinia(pinia);vi.spyOn(useSessionStore(),'can').mockReturnValue(allowed);
  const w=mount(Dialog,{props:{open:true,knowledgeBaseId:id(2),workspace:existing?workspace():undefined,selection:existing?undefined:{embeddingModelConfigId:id(13),embeddingModelVersion:1,sources:[source]},initialQuestion:'冻结问题'},global:{plugins:[pinia],stubs:{Teleport:true,ArtFormDialog:{template:'<div><slot /></div>'},AiKnowledgeBaseSelect:true,AiKnowledgeSourceDialog:true}}});wrappers.push(w);await flushPromises();return w;}
async function submit(w:VueWrapper){await w.get('form').trigger('submit');await flushPromises();}
beforeEach(()=>{vi.clearAllMocks();vi.mocked(api.saveFaqDraft).mockResolvedValue({workspace:workspace(),savedRevisionId:id(4),replayed:false});vi.mocked(api.getFaqDraft).mockResolvedValue(workspace());vi.mocked(api.listFaqRevisions).mockResolvedValue({items:[workspace().revision],nextAfterId:null});});
afterEach(()=>{wrappers.splice(0).forEach(w=>w.unmount());vi.restoreAllMocks();});
describe('FAQ 草稿维护',()=>{
  it('派生来源只读，保存冻结头，不派发收费',async()=>{const w=await render();expect(w.find('[data-testid="faq-basis"]').exists()).toBe(false);expect(api.saveFaqDraft).not.toHaveBeenCalled();await submit(w);expect(vi.mocked(api.saveFaqDraft).mock.calls[0]![0]).toMatchObject({faqId:id(1),body:{expectedRevisionId:id(4),expectedHeadVersion:1}});expect(w.get('[data-testid="faq-saved"]').text()).toContain('尚未发布');});
  it('未知响应显式重试复用请求键，输入变化建立新意图',async()=>{vi.mocked(api.saveFaqDraft).mockRejectedValue(Error('unknown'));const w=await render();await submit(w);await submit(w);const calls=vi.mocked(api.saveFaqDraft).mock.calls;expect(calls[0]![0].body.requestKey).toBe(calls[1]![0].body.requestKey);await w.get('[data-testid="faq-answer"]').setValue('编辑后答案');await submit(w);expect(calls[2]![0].body.requestKey).not.toBe(calls[0]![0].body.requestKey);});
  it('冲突保留未保存输入，必须显式重读才允许保存',async()=>{vi.mocked(api.saveFaqDraft).mockRejectedValue({type:'about:blank',title:'conflict',status:409,code:'ai.knowledge_faqs.version_conflict'});const w=await render();await w.get('[data-testid="faq-answer"]').setValue('未保存');await submit(w);expect((w.get('[data-testid="faq-answer"]').element as HTMLTextAreaElement).value).toBe('未保存');expect(w.get('[data-testid="faq-save"]').attributes('disabled')).toBeDefined();await w.get('[data-testid="faq-reload"]').trigger('click');await flushPromises();expect(api.getFaqDraft).toHaveBeenCalled();expect((w.get('[data-testid="faq-answer"]').element as HTMLTextAreaElement).value).toBe('答案');});
  it('来源失效清空正文并通知父页面',async()=>{vi.mocked(api.saveFaqDraft).mockRejectedValue({type:'about:blank',title:'unavailable',status:409,code:'ai.knowledge_faqs.source_unavailable'});const w=await render();await submit(w);expect(w.find('textarea').exists()).toBe(false);expect(w.emitted('unavailable')).toHaveLength(1);});
  it('从搜索创建使用完整来源，不发送客户端来源类型',async()=>{vi.mocked(api.createDocumentFaq).mockResolvedValue({workspace:workspace(),savedRevisionId:id(4),replayed:false});const w=await render(false);await w.get('[data-testid="faq-answer"]').setValue('手工核对答案');await w.get('[data-testid="faq-category"]').setValue('制度');await submit(w);expect(vi.mocked(api.createDocumentFaq).mock.calls[0]![0].body).toMatchObject({content:{question:'冻结问题'},selection:{sources:[source]}});});
  it('历史只显式读取，恢复绑定当前头且不发送来源覆盖',async()=>{vi.mocked(api.restoreFaqDraft).mockResolvedValue({workspace:workspace(),savedRevisionId:id(4),replayed:false});const w=await render();expect(api.listFaqRevisions).not.toHaveBeenCalled();await w.get('[data-testid="faq-history"]').trigger('click');await flushPromises();await w.get('[data-testid="faq-restore"]').trigger('click');await flushPromises();expect(vi.mocked(api.restoreFaqDraft).mock.calls[0]![0].body).toMatchObject({expectedRevisionId:id(4),expectedHeadVersion:1,revisionId:id(4)});});
  it('无权限不发送草稿读取或写入',async()=>{const w=await render(true,false);expect(w.find('form').exists()).toBe(false);expect(api.getFaqDraft).not.toHaveBeenCalled();});
  it('关闭取消写入且迟到响应不能恢复正文',async()=>{let resolve!:(v:Awaited<ReturnType<typeof api.saveFaqDraft>>)=>void;vi.mocked(api.saveFaqDraft).mockImplementation(()=>new Promise(r=>resolve=r));const w=await render();await w.get('form').trigger('submit');const signal=vi.mocked(api.saveFaqDraft).mock.calls[0]![2];await w.setProps({open:false});resolve({workspace:workspace(),savedRevisionId:id(4),replayed:false});await flushPromises();expect(signal?.aborted).toBe(true);expect(w.emitted('saved')).toBeUndefined();});
  it('真实 FAQ 选择走专属派生入口，不退回文档或独立创建',async()=>{
    const w=await render(false);const parent={...source,sourceKind:'faq-revision-v1',faqId:id(40),faqRevisionId:id(41)};
    const current={...workspace(),origin:'faq_derived',sources:[parent],graph:{roots:[parent.documentId],nodes:[{source:parent,parentDocumentIds:[]}]}};
    vi.mocked(api.createDerivedFaq).mockResolvedValue({workspace:current,savedRevisionId:current.revision.id,replayed:false});
    await w.setProps({selection:{embeddingModelConfigId:id(13),embeddingModelVersion:1,sources:[parent]}});await flushPromises();
    await w.get('[data-testid="faq-question"]').setValue('父派生问题');await w.get('[data-testid="faq-answer"]').setValue('受控派生答案');await w.get('[data-testid="faq-category"]').setValue('制度');
    await w.get('form').trigger('submit');await flushPromises();expect(api.createDerivedFaq).toHaveBeenCalledTimes(1);expect(api.createDocumentFaq).not.toHaveBeenCalled();expect(api.createIndependentFaq).not.toHaveBeenCalled();
    expect(w.get('[data-testid="faq-parent-graph"]').text()).toContain(parent.faqId);expect(w.find('[data-testid="faq-basis"]').exists()).toBe(false);
  });});

it('附加日程先走正式预览，保存只携带准确身份且复制后保留来源',async()=>{
  const personal={id:id(60),version:3,projectionHash:'D'.repeat(64)};
  vi.mocked(previewPersonalScheduleSources).mockResolvedValue({classification:'restricted',sources:[{...personal,kind:'calendar-personal-schedule-v1',purpose:'knowledge-assistant-v1',content:'私人日程正文',startAtUtc:'2026-10-30T00:00:00Z',endAtUtc:'2026-10-30T01:00:00Z',status:'pending'}]});
  const saved={...workspace(),personalSchedules:[personal]};vi.mocked(api.createDocumentFaq).mockResolvedValue({workspace:saved,savedRevisionId:saved.revision.id,replayed:false});
  const w=await render(false);await w.get('[data-testid="faq-schedule-id"]').setValue(personal.id);await w.get('[data-testid="faq-capture-schedule"]').trigger('click');await flushPromises();
  expect(previewPersonalScheduleSources).toHaveBeenCalledWith([personal.id],expect.any(AbortSignal));
  await w.get('[data-testid="faq-answer"]').setValue('人工标准答案');await w.get('[data-testid="faq-category"]').setValue('私人');await submit(w);
  const request=vi.mocked(api.createDocumentFaq).mock.calls[0]![0];expect(request.body.selection.personalSchedules).toEqual([personal]);expect(JSON.stringify(request)).not.toContain('私人日程正文');
  expect(w.get('[data-testid="faq-business-sources"]').text()).toContain(personal.id);expect(w.find('[data-testid="faq-capture-schedule"]').exists()).toBe(false);
});
it('附加来源拒绝或迟到不保留原正文',async()=>{
  vi.mocked(previewPersonalScheduleSources).mockRejectedValue({status:409,code:'calendar.personal_schedule_source_unavailable'});
  const w=await render(false);await w.get('[data-testid="faq-answer"]').setValue('原待保存正文');await w.get('[data-testid="faq-schedule-id"]').setValue(id(60));
  await w.get('[data-testid="faq-capture-schedule"]').trigger('click');await flushPromises();expect(w.find('textarea').exists()).toBe(false);expect(w.emitted('unavailable')).toHaveLength(1);
});
