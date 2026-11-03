import {afterEach,beforeEach,describe,expect,it,vi} from 'vitest';
import {mount,flushPromises,type VueWrapper} from '@vue/test-utils';
import {createPinia,setActivePinia} from 'pinia';
import Dialog from './AiKnowledgeFaqFeedbackDialog.vue';
import {useSessionStore} from '../auth/session';
import {getKnowledgeBase} from '../api/ai-knowledge';
import * as api from '../api/ai-knowledge-faq-feedback';
vi.mock('../api/ai-knowledge',()=>({getKnowledgeBase:vi.fn()}));
vi.mock('../api/ai-knowledge-faq-feedback',()=>({previewFeedbackFaq:vi.fn(),createFeedbackFaq:vi.fn()}));
const id=(n:number)=>`01900000-0000-7000-8000-${String(n).padStart(12,'0')}`;
const feedback={feedbackId:id(1),ratingKey:'helpful',reasonKey:'accurate',citationCount:0,feedbackVersion:1,reviewStateKey:'approved',reviewVersion:2};
const source={knowledgeBaseId:id(2),knowledgeBaseVersion:1,documentId:id(3),documentVersion:1,versionId:id(4),publicationId:id(5),generationId:id(6),manifestHash:'A'.repeat(64)};
const preview=()=>({digest:'B'.repeat(64),sourceHash:'C'.repeat(64),feedback:{feedbackId:id(1),feedbackVersion:1,reviewId:id(7),reviewVersion:2,messageId:id(8),lineageHash:'D'.repeat(64),answerHash:'E'.repeat(64)},answer:'服务器当前私人回答',sources:[source],graph:null});
const workspace=()=>({id:id(9),knowledgeBaseId:id(10),reservedDocumentId:id(11),state:'draft',origin:'feedback_derived',basis:null,sourceHash:preview().sourceHash,sources:[source],feedback:preview().feedback,headVersion:1,
  revision:{id:id(12),number:1,content:{question:'人工标准问题',aliases:[],answer:preview().answer,category:'制度',validFromUtc:null,expiresAtUtc:null},contentHash:'F'.repeat(64),createdAtUtc:'2026-10-30T00:00:00Z',restoredFromRevisionId:null}});
const wrappers:VueWrapper[]=[];
async function render(allowFeedback=true){const pinia=createPinia();setActivePinia(pinia);vi.spyOn(useSessionStore(),'can').mockImplementation(code=>allowFeedback||code!=='ai.knowledge_faqs.feedback');
  const w=mount(Dialog,{props:{feedback},global:{plugins:[pinia],stubs:{Teleport:true,ArtFormDialog:{props:['open'],template:'<div v-if="open"><slot /></div>'},AiKnowledgeFaqDraftDialog:true,
    AiKnowledgeBaseSelect:{name:'AiKnowledgeBaseSelect',props:['modelValue'],emits:['update:modelValue'],template:'<input data-testid="target" :value="modelValue" @input="$emit(\'update:modelValue\',$event.target.value)" />'}}}});wrappers.push(w);await flushPromises();return w;}
async function open(w:VueWrapper){await w.get('[data-testid="faq-feedback-open"]').trigger('click');await w.get('[data-testid="target"]').setValue(id(10));await flushPromises();}
async function read(w:VueWrapper){await w.get('[data-testid="faq-feedback-preview"]').trigger('click');await flushPromises();}
async function fill(w:VueWrapper){await w.get('[data-testid="faq-feedback-question"]').setValue('人工标准问题');await w.get('[data-testid="faq-feedback-category"]').setValue('制度');}
async function save(w:VueWrapper){await w.get('form').trigger('submit');await flushPromises();}
beforeEach(()=>{vi.clearAllMocks();vi.mocked(getKnowledgeBase).mockResolvedValue({id:id(10),dataClassification:'restricted'} as Awaited<ReturnType<typeof getKnowledgeBase>>);
  vi.mocked(api.previewFeedbackFaq).mockResolvedValue(preview());vi.mocked(api.createFeedbackFaq).mockResolvedValue({workspace:workspace(),savedRevisionId:id(12),replayed:false});});
afterEach(()=>{wrappers.splice(0).forEach(w=>w.unmount());vi.restoreAllMocks();});
describe('批准反馈到 FAQ 草稿',()=>{
  it('只有审核读取权限不足以打开私人来源，也不会提前读取原回答',async()=>{const w=await render(false);expect(w.find('[data-testid="faq-feedback-open"]').exists()).toBe(false);expect(api.previewFeedbackFaq).not.toHaveBeenCalled();});
  it('先显式预览，人工填写问题；创建请求没有客户端答案或引用子集',async()=>{const w=await render();expect(api.previewFeedbackFaq).not.toHaveBeenCalled();await open(w);await read(w);
    expect(w.get('[data-testid="faq-feedback-answer"]').attributes('readonly')).toBeDefined();await fill(w);await save(w);
    const p=vi.mocked(api.createFeedbackFaq).mock.calls[0]![0];expect(p.body).toMatchObject({feedbackId:id(1),digest:'B'.repeat(64),content:{question:'人工标准问题',category:'制度'}});
    expect(p.body.content).not.toHaveProperty('answer');expect(p.body).not.toHaveProperty('citationIds');expect(p.body.requestKey).toMatch(/-7[0-9a-f]{3}-/);expect(w.get('[data-testid="faq-feedback-saved"]').text()).toContain('尚未索引');});
  it('目标不是 restricted 时不发送原回答预览请求',async()=>{vi.mocked(getKnowledgeBase).mockResolvedValue({id:id(10),dataClassification:'internal'} as Awaited<ReturnType<typeof getKnowledgeBase>>);const w=await render();await open(w);await read(w);expect(api.previewFeedbackFaq).not.toHaveBeenCalled();expect(w.find('[data-testid="faq-feedback-answer"]').exists()).toBe(false);});
  it('反馈或审核版本变化时清空返回正文并要求刷新队列',async()=>{vi.mocked(api.previewFeedbackFaq).mockResolvedValue({...preview(),feedback:{...preview().feedback,reviewVersion:3}});const w=await render();await open(w);await read(w);expect(w.find('[data-testid="faq-feedback-answer"]').exists()).toBe(false);expect(w.emitted('unavailable')).toHaveLength(1);});
  it('未知保存显式重试复用原请求键，同时先清空原回答',async()=>{vi.mocked(api.createFeedbackFaq).mockRejectedValue(Error('unconfirmed'));const w=await render();await open(w);await read(w);await fill(w);await save(w);
    expect(w.find('[data-testid="faq-feedback-answer"]').exists()).toBe(false);expect(api.createFeedbackFaq).toHaveBeenCalledTimes(1);await save(w);
    const calls=vi.mocked(api.createFeedbackFaq).mock.calls;expect(calls[1]![0].body.requestKey).toBe(calls[0]![0].body.requestKey);expect(calls[1]![0].body.content).not.toHaveProperty('answer');});
  it('保存后的来源撤回清空预览、编辑入口和待重试请求',async()=>{const w=await render();await open(w);await read(w);await fill(w);await save(w);vi.mocked(api.previewFeedbackFaq).mockRejectedValue({type:'about:blank',title:'unavailable',status:409,code:'ai.knowledge_faqs.source_unavailable'});
    await read(w);expect(w.find('[data-testid="faq-feedback-answer"]').exists()).toBe(false);expect(w.find('[data-testid="faq-feedback-edit"]').exists()).toBe(false);expect(w.emitted('unavailable')).toHaveLength(1);});
  it('切换目标后忽略不遵守取消的迟到正文',async()=>{let resolve!:(value:ReturnType<typeof preview>)=>void;vi.mocked(api.previewFeedbackFaq).mockImplementation(()=>new Promise(r=>{resolve=r;}));const w=await render();await open(w);await w.get('[data-testid="faq-feedback-preview"]').trigger('click');await flushPromises();
    // 加载时选择器禁用；模拟其作用域观察器主动清空目标，不以被禁用输入的 DOM 事件假装发生切换。
    w.findComponent({name:'AiKnowledgeBaseSelect'}).vm.$emit('update:modelValue',id(13));await flushPromises();resolve(preview());await flushPromises();expect(w.find('[data-testid="faq-feedback-answer"]').exists()).toBe(false);expect(api.createFeedbackFaq).not.toHaveBeenCalled();});
});
