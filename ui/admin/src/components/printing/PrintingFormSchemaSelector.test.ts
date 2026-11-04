import {afterEach,beforeEach,expect,it,vi} from 'vitest';
import {flushPromises,mount,type VueWrapper} from '@vue/test-utils';
import Selector from './PrintingFormSchemaSelector.vue';
import * as api from '../../api/printing-templates';
import {createOutputSession} from '../../test/data-output-fixtures';
import type {PrintingFormSchemaDefinition} from '@fullnet/client-contracts';
vi.mock('../../api/printing-templates',()=>({listPrintingFormSchemas:vi.fn()}));
const permission='printing.form_schemas.read',profile={formSchemaKey:'printing.tenant_profile_card',displayName:'Profile',description:'Profile fields',requiresRecordId:false,fields:[{fieldKey:'tenantName',displayName:'Tenant'}]},business={formSchemaKey:'demo.enterprise_request.summary',displayName:'Business',description:'Owned record',requiresRecordId:true,fields:[{fieldKey:'requestNumber',displayName:'Number'}]};
let wrapper:VueWrapper;
type State={rows:PrintingFormSchemaDefinition[];failed:boolean;loading:boolean;controls:{select:(value:string)=>void;replace:()=>void;reload:()=>void}};
const state=()=>wrapper.vm as unknown as State;
beforeEach(()=>{vi.resetAllMocks();vi.mocked(api.listPrintingFormSchemas).mockResolvedValue([profile,business]);});afterEach(()=>{wrapper?.unmount();vi.useRealTimers();});
async function setup({permissions=[permission],immutable=false,disabled=false,host=true}={}){
 const context=createOutputSession(permissions);if(host)context.session.currentUser={...context.session.currentUser!,tenantId:null,scope:'host',actorScope:'host'};
 wrapper=mount(Selector,{props:{modelValue:profile.formSchemaKey,layoutHtml:'<p>Keep my layout</p>',immutable,disabled},global:{plugins:[context.pinia]}});await flushPromises();return context;
}
it('选择已注册业务表单只更新绑定，不替换版式或自动保存',async()=>{
 await setup();expect(state().rows).toHaveLength(2);const stale=state().controls.select;state().controls.select(business.formSchemaKey);expect(wrapper.emitted('update:modelValue')).toEqual([[business.formSchemaKey]]);expect(wrapper.emitted('replace')).toBeUndefined();stale(profile.formSchemaKey);expect(wrapper.emitted('update:modelValue')).toHaveLength(1);
 await wrapper.setProps({modelValue:business.formSchemaKey});expect(wrapper.text()).toContain('业务记录');state().controls.replace();expect(wrapper.emitted('replace')?.[0]?.[0]).toMatchObject({formSchemaKey:business.formSchemaKey,layoutHtml:expect.stringContaining('{{requestNumber}}')});
});
it('目录等待期间编辑版式不丢弃响应，也不能自动替换本地文本',async()=>{
 let release!:(rows:PrintingFormSchemaDefinition[])=>void;vi.mocked(api.listPrintingFormSchemas).mockImplementationOnce(()=>new Promise(resolve=>{release=resolve;}));await setup();await wrapper.setProps({layoutHtml:'<p>Changed while reading</p>'});release([profile,business]);await flushPromises();expect(state().rows).toHaveLength(2);expect((wrapper.props() as {layoutHtml:string}).layoutHtml).toBe('<p>Changed while reading</p>');expect(wrapper.emitted('replace')).toBeUndefined();
});
it.each([{permissions:[]},{immutable:true},{host:false}])('无目录权限、已有模板或租户上下文不能选择其他绑定 %j',async options=>{
 await setup(options);expect(api.listPrintingFormSchemas).not.toHaveBeenCalled();state().controls.select(business.formSchemaKey);state().controls.replace();expect(wrapper.emitted('update:modelValue')).toBeUndefined();expect(wrapper.emitted('replace')).toBeUndefined();expect(wrapper.text()).toContain(profile.formSchemaKey);
});
it('读取失败保留绑定和版式，人工重读后旧选项回调失效',async()=>{
 vi.mocked(api.listPrintingFormSchemas).mockRejectedValueOnce(Error('network unavailable'));await setup();expect(state().failed).toBe(true);expect((wrapper.props() as {modelValue:string}).modelValue).toBe(profile.formSchemaKey);const stale=state().controls.select;state().controls.reload();await flushPromises();expect(state().failed).toBe(false);stale(business.formSchemaKey);expect(wrapper.emitted('update:modelValue')).toBeUndefined();state().controls.select(business.formSchemaKey);expect(wrapper.emitted('update:modelValue')).toHaveLength(1);
});
it('换表单、输入变化或禁用后，旧版式替换确认不能借用新状态',async()=>{
 await setup();const oldLayout=state().controls.replace;await wrapper.setProps({layoutHtml:'Changed'});oldLayout();expect(wrapper.emitted('replace')).toBeUndefined();const oldSchema=state().controls.replace;await wrapper.setProps({modelValue:business.formSchemaKey});oldSchema();expect(wrapper.emitted('replace')).toBeUndefined();const active=state().controls.replace;await wrapper.setProps({disabled:true});active();state().controls.replace();expect(wrapper.emitted('replace')).toBeUndefined();
});
it('撤权或卸载取消目录请求，迟到目录不能恢复旧选项',async()=>{
 let release!:(rows:PrintingFormSchemaDefinition[])=>void;vi.mocked(api.listPrintingFormSchemas).mockImplementationOnce(()=>new Promise(resolve=>{release=resolve;}));const context=await setup();const signal=vi.mocked(api.listPrintingFormSchemas).mock.calls[0]![0]!;context.session.currentUser={...context.session.currentUser!,permissions:[]};expect(signal.aborted).toBe(true);release([profile,business]);await flushPromises();expect(state().rows).toEqual([]);
});
it('非合作目录读取十秒后停止等待，迟到响应仍不能接入',async()=>{
 vi.useFakeTimers();let release!:(rows:PrintingFormSchemaDefinition[])=>void;vi.mocked(api.listPrintingFormSchemas).mockImplementationOnce(()=>new Promise(resolve=>{release=resolve;}));await setup();await vi.advanceTimersByTimeAsync(10001);expect(state().loading).toBe(false);expect(state().failed).toBe(true);release([profile,business]);await flushPromises();expect(state().rows).toEqual([]);
});
