import {afterEach,beforeEach,describe,expect,it,vi} from 'vitest';
import {mount,flushPromises,type VueWrapper} from '@vue/test-utils';
import {createPinia,setActivePinia} from 'pinia';
import View from './AiKnowledgeFaqsView.vue';
import {useSessionStore} from '../auth/session';
import * as api from '../api/ai-knowledge-faqs';
vi.mock('../api/ai-knowledge-faqs',()=>({listFaqDrafts:vi.fn(),getFaqDraft:vi.fn()}));
const id=(n:number)=>`01900000-0000-7000-8000-${String(n).padStart(12,'0')}`;
const workspace=()=>({id:id(1),knowledgeBaseId:id(2),reservedDocumentId:id(3),state:'draft',origin:'independent',basis:'依据',sourceHash:'A'.repeat(64),sources:[],headVersion:1,
  revision:{id:id(4),number:1,content:{question:'可见问题',aliases:[],answer:'答案',category:'制度',validFromUtc:null,expiresAtUtc:null},contentHash:'B'.repeat(64),createdAtUtc:'2026-10-29T00:00:00Z',restoredFromRevisionId:null}});
const wrappers:VueWrapper[]=[];
async function render(allow=(code:string)=>!!code){const pinia=createPinia();setActivePinia(pinia);vi.spyOn(useSessionStore(),'can').mockImplementation(allow);const w=mount(View,{global:{plugins:[pinia],stubs:{AiKnowledgeFaqDraftDialog:true,AiKnowledgeBaseSelect:{template:`<button data-testid="choose" @click="$emit('update:modelValue','${id(2)}')">选择</button>`}}}});wrappers.push(w);await flushPromises();return w;}
beforeEach(()=>{vi.clearAllMocks();vi.mocked(api.listFaqDrafts).mockResolvedValue({items:[workspace()],nextAfterId:null});vi.mocked(api.getFaqDraft).mockResolvedValue(workspace());});afterEach(()=>{wrappers.splice(0).forEach(w=>w.unmount());vi.restoreAllMocks();});
describe('FAQ 工作台来源读取边界',()=>{
  it('读取已有草稿前重新核对来源，不直接使用列表缓存正文',async()=>{const w=await render();await w.get('[data-testid="choose"]').trigger('click');await flushPromises();expect(w.findAll('[data-testid="faq-list-item"]')).toHaveLength(1);await w.get('[data-testid="faq-open"]').trigger('click');await flushPromises();expect(api.getFaqDraft).toHaveBeenCalledWith({knowledgeBaseId:id(2),faqId:id(1)},expect.any(AbortSignal));expect(w.findComponent({name:'AiKnowledgeFaqDraftDialog'}).props('open')).toBe(true);});
  it('读取拒绝清空旧列表内容',async()=>{const w=await render();await w.get('[data-testid="choose"]').trigger('click');await flushPromises();vi.mocked(api.getFaqDraft).mockRejectedValue(Error('revoked'));await w.get('[data-testid="faq-open"]').trigger('click');await flushPromises();expect(w.findAll('[data-testid="faq-list-item"]')).toHaveLength(0);expect(w.find('[data-testid="faq-list-error"]').exists()).toBe(true);});
  it('无写权限可读但不开放创建',async()=>{const w=await render(code=>code!=='ai.knowledge_faqs.write');await w.get('[data-testid="choose"]').trigger('click');await flushPromises();expect(w.findAll('[data-testid="faq-list-item"]')).toHaveLength(1);expect(w.find('[data-testid="faq-create"]').exists()).toBe(false);});
});
