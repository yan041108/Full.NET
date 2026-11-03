import {beforeEach,afterEach,describe,it,expect,vi} from 'vitest';
import {mount,flushPromises,type VueWrapper} from '@vue/test-utils';
import {createPinia,setActivePinia} from 'pinia';
import View from './AiKnowledgeOperationsView.vue';
import {useSessionStore} from '../auth/session';
import {listKnowledgeOperations} from '../api/ai-knowledge-operations';
import type {AiKnowledgeOperationsPage} from '@fullnet/client-contracts';
vi.mock('../api/ai-knowledge-operations',()=>({listKnowledgeOperations:vi.fn()}));
const id=(n:number)=>`01900000-0000-7000-8000-${String(n).padStart(12,'0')}`;
const page=():AiKnowledgeOperationsPage=>({items:[{knowledgeBaseId:id(1),documentId:id(2),versionId:id(3),versionNumber:1,sourceKind:'document-v1',faqId:null,
  preparationState:'completed',taskId:id(4),taskState:'completed',attemptCount:1,failureCode:null,createdAtUtc:'2026-10-29T00:00:00Z',taskUpdatedAtUtc:null,
  hasActiveGeneration:true,reviewState:'none',publicationId:null,publicationVersionId:null,publicationState:'none',effectiveFromUtc:null,effectiveToUtc:null}],
  nextAfterId:null,fromUtc:'2026-10-01T00:00:00Z',toUtc:'2026-10-30T00:00:00Z',view:'all'});
const wrappers:VueWrapper[]=[];
async function render(allow=true){const pinia=createPinia();setActivePinia(pinia);vi.spyOn(useSessionStore(),'can').mockImplementation(code=>allow||code!=='ai.knowledge_operations.read');
  const w=mount(View,{global:{plugins:[pinia],stubs:{AiKnowledgeBaseSelect:{name:'AiKnowledgeBaseSelect',props:['modelValue'],emits:['update:modelValue'],template:'<input data-testid="base" :value="modelValue" @input="$emit(\'update:modelValue\',$event.target.value)" />'},AiKnowledgeIndexDialog:true,AiKnowledgeFaqLifecycleDialog:true,AiKnowledgeQualityPanel:true}}});wrappers.push(w);await flushPromises();return w;}
async function load(w:VueWrapper){await w.get('form').trigger('submit');await flushPromises();}
beforeEach(()=>{vi.clearAllMocks();vi.mocked(listKnowledgeOperations).mockResolvedValue(page());});afterEach(()=>{wrappers.splice(0).forEach(w=>w.unmount());vi.restoreAllMocks();});
describe('维护者运营视图',()=>{
  it('普通读者缺少独立运营权限，不显示查询入口或读取诊断',async()=>{const w=await render(false);expect(w.find('[data-testid="operations-refresh"]').exists()).toBe(false);expect(listKnowledgeOperations).not.toHaveBeenCalled();});
  it('完成状态不显示为已发布或自动触发处置',async()=>{const w=await render();expect(listKnowledgeOperations).not.toHaveBeenCalled();await load(w);
    expect(w.get('[data-testid="operations-task-state"]').text()).toContain('处理完成');expect(w.get('[data-testid="operations-publication-state"]').text()).toBe('无记录');expect(w.find('[data-testid="operations-inspect"]').exists()).toBe(true);expect(w.text()).toContain('不推算');});
  it('下一页复用原时间窗口和准确版本游标',async()=>{vi.mocked(listKnowledgeOperations).mockResolvedValue({...page(),nextAfterId:id(3)});const w=await render();await load(w);await w.get('[data-testid="operations-next"]').trigger('click');await flushPromises();
    expect(vi.mocked(listKnowledgeOperations).mock.calls[1]![0]).toMatchObject({fromUtc:page().fromUtc,toUtc:page().toUtc,afterId:id(3)});});
  it('查询失败清空旧记录和处置窗口',async()=>{const w=await render();await load(w);await w.get('[data-testid="operations-inspect"]').trigger('click');vi.mocked(listKnowledgeOperations).mockRejectedValue({status:403});await load(w);
    expect(w.findAll('[data-testid="operations-row"]')).toHaveLength(0);expect(w.find('[data-testid="operations-error"]').exists()).toBe(true);expect(w.find('ai-knowledge-index-dialog-stub').exists()).toBe(false);});
  it('范围切换忽略未遵守取消的迟到响应',async()=>{let resolve!:(v:AiKnowledgeOperationsPage)=>void;vi.mocked(listKnowledgeOperations).mockImplementation(()=>new Promise(r=>{resolve=r;}));const w=await render();await w.get('form').trigger('submit');useSessionStore().switching=true;await flushPromises();resolve(page());await flushPromises();expect(w.findAll('[data-testid="operations-row"]')).toHaveLength(0);});
  it('修改知识库筛选立即清除旧页及游标',async()=>{const w=await render();await load(w);await w.get('[data-testid="base"]').setValue(id(9));await flushPromises();expect(w.findAll('[data-testid="operations-row"]')).toHaveLength(0);expect(w.find('[data-testid="operations-next"]').exists()).toBe(false);});
});
