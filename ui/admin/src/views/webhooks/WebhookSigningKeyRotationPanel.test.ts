import { afterEach,beforeEach,expect,it,vi } from 'vitest';
import { mount,flushPromises,type VueWrapper } from '@vue/test-utils';
import Panel from './WebhookSigningKeyRotationPanel.vue';
import * as api from '../../api/webhook-subscriptions';
import { createOutputSession,outputId } from '../../test/data-output-fixtures';
import type { WebhookSigningKeyRotationReceipt } from '@fullnet/client-contracts';
vi.mock('../../api/webhook-subscriptions',()=>({getPendingWebhookSigningKeyRotation:vi.fn(),getWebhookSigningKeyRotation:vi.fn(),prepareWebhookSigningKeyRotation:vi.fn(),activateWebhookSigningKeyRotation:vi.fn(),cancelWebhookSigningKeyRotation:vi.fn()}));
const read='webhooks.subscriptions.read',write='webhooks.subscriptions.rotate_signing_key',next='019bc2b1-2a40-7cc3-8992-a80de51bf298';
const subscription={id:outputId,tenantId:outputId,signingKeyId:outputId,eventType:'fullnet.workflow.instance.completed',targetUrl:'https://example.com/hook',isActive:true,version:1};
const prepared:WebhookSigningKeyRotationReceipt={operationId:outputId,tenantId:outputId,subscriptionId:outputId,previousKeyId:outputId,signingKeyId:next,previousVersion:1,preparedVersion:2,stateKey:'prepared',preparedAtUtc:'2026-11-05T00:00:00Z',completedAtUtc:null,completedVersion:null};
const activated:WebhookSigningKeyRotationReceipt={...prepared,stateKey:'activated',completedAtUtc:'2026-11-05T00:01:00Z',completedVersion:3};
const missing={type:'about:blank',title:'Missing',status:404,code:'webhooks.signing_key_rotation.not_found'};
let wrapper:VueWrapper;
type Controls={input:(value:string)=>void;prepare:()=>void;activate:()=>void;cancel:()=>void;confirm:()=>void;acknowledge:(value:boolean)=>void;read:()=>void;close:()=>void};
type State={secret:string;receipt?:WebhookSigningKeyRotationReceipt;unknown:boolean;busy:boolean;confirmation?:unknown;controls:Controls};
const state=()=>wrapper.vm as unknown as State;
beforeEach(()=>{vi.resetAllMocks();vi.mocked(api.getPendingWebhookSigningKeyRotation).mockRejectedValue(missing);});afterEach(()=>wrapper?.unmount());
async function setup(permissions=[read,write]){const context=createOutputSession(permissions);wrapper=mount(Panel,{props:{modelValue:true,subscription},global:{plugins:[context.pinia],stubs:{ElDialog:{props:['modelValue'],template:'<section v-if="modelValue"><slot/><slot name="footer"/></section>'}}}});await flushPromises();return context;}

it('准备清空秘密且不激活；核对接收端后才沿用新身份明确激活',async()=>{
 vi.mocked(api.prepareWebhookSigningKeyRotation).mockImplementation(async(_id,request)=>({...prepared,operationId:request.operationId}));await setup();state().controls.input('new-independent-secret');state().controls.prepare();await flushPromises();
 expect(state().secret).toBe('');expect(api.activateWebhookSigningKeyRotation).not.toHaveBeenCalled();expect(state().receipt?.stateKey).toBe('prepared');
 await wrapper.setProps({subscription:{...subscription,version:2}});state().controls.activate();state().controls.confirm();expect(api.activateWebhookSigningKeyRotation).not.toHaveBeenCalled();
 vi.mocked(api.activateWebhookSigningKeyRotation).mockResolvedValue({...activated,operationId:state().receipt!.operationId});state().controls.acknowledge(true);state().controls.confirm();await flushPromises();
 expect(api.activateWebhookSigningKeyRotation).toHaveBeenCalledExactlyOnceWith(outputId,state().receipt!.operationId,{version:2,signingKeyId:next},expect.any(AbortSignal));expect(state().receipt?.stateKey).toBe('activated');
});
it('独立只读权限可读准备；撤销写权限后保留回执但不能确认',async()=>{
 vi.mocked(api.getPendingWebhookSigningKeyRotation).mockResolvedValue(prepared);await setup([read]);expect(state().receipt).toEqual(prepared);expect(wrapper.find('[data-testid="webhook-key-secret"]').exists()).toBe(false);state().controls.activate();expect(state().confirmation).toBeUndefined();expect(api.activateWebhookSigningKeyRotation).not.toHaveBeenCalled();
});
it('关闭在途准备清空秘密，晚到响应不能接入，404 后仍沿用原操作与修订',async()=>{
 let release!:(value:WebhookSigningKeyRotationReceipt)=>void;vi.mocked(api.prepareWebhookSigningKeyRotation).mockImplementationOnce(()=>new Promise(resolve=>{release=resolve;}));await setup();const staleInput=state().controls.input;state().controls.input('new-independent-secret');state().controls.prepare();await flushPromises();const original=vi.mocked(api.prepareWebhookSigningKeyRotation).mock.calls[0]![1];
 state().controls.close();await wrapper.setProps({modelValue:false});staleInput('late-private-secret');release({...prepared,operationId:original.operationId});await flushPromises();expect(state().secret).toBe('');expect(state().receipt).toBeUndefined();expect(state().unknown).toBe(true);
 vi.mocked(api.getWebhookSigningKeyRotation).mockRejectedValue(missing);await wrapper.setProps({modelValue:true});await flushPromises();expect(state().unknown).toBe(true);expect(api.prepareWebhookSigningKeyRotation).toHaveBeenCalledTimes(1);
 vi.mocked(api.prepareWebhookSigningKeyRotation).mockResolvedValue({...prepared,operationId:original.operationId});state().controls.input('new-independent-secret');state().controls.prepare();await flushPromises();expect(vi.mocked(api.prepareWebhookSigningKeyRotation).mock.calls[1]![1]).toEqual(original);expect(state().unknown).toBe(false);
});
it('未知激活读到 prepared 不能误判；重试保留原确认修订而不取后来版本',async()=>{
 vi.mocked(api.getPendingWebhookSigningKeyRotation).mockResolvedValue(prepared);vi.mocked(api.activateWebhookSigningKeyRotation).mockRejectedValueOnce(Error('connection lost'));await setup();await wrapper.setProps({subscription:{...subscription,version:2}});state().controls.activate();state().controls.acknowledge(true);state().controls.confirm();await flushPromises();expect(state().unknown).toBe(true);
 vi.mocked(api.getWebhookSigningKeyRotation).mockResolvedValue(prepared);state().controls.read();await flushPromises();expect(state().unknown).toBe(true);expect(api.activateWebhookSigningKeyRotation).toHaveBeenCalledTimes(1);
 await wrapper.setProps({subscription:{...subscription,version:4}});vi.mocked(api.activateWebhookSigningKeyRotation).mockResolvedValue(activated);state().controls.activate();state().controls.acknowledge(true);state().controls.confirm();await flushPromises();expect(vi.mocked(api.activateWebhookSigningKeyRotation).mock.calls[1]![2]).toEqual({version:2,signingKeyId:next});expect(state().unknown).toBe(false);
});
it('原修订变化使旧确认失效，取消独立确认且不激活',async()=>{
 vi.mocked(api.getPendingWebhookSigningKeyRotation).mockResolvedValue(prepared);await setup();await wrapper.setProps({subscription:{...subscription,version:2}});state().controls.activate();state().controls.acknowledge(true);const stale=state().controls.confirm;await wrapper.setProps({subscription:{...subscription,version:3}});stale();expect(api.activateWebhookSigningKeyRotation).not.toHaveBeenCalled();
 vi.mocked(api.cancelWebhookSigningKeyRotation).mockResolvedValue({...prepared,stateKey:'cancelled',completedAtUtc:activated.completedAtUtc,completedVersion:4});state().controls.cancel();expect(api.cancelWebhookSigningKeyRotation).not.toHaveBeenCalled();state().controls.confirm();await flushPromises();expect(api.cancelWebhookSigningKeyRotation).toHaveBeenCalledExactlyOnceWith(outputId,prepared.operationId,{version:3,signingKeyId:next},expect.any(AbortSignal));
});
it('当前主体变化清除旧轮换及敏感输入，迟到回执不能接入新主体',async()=>{
 let release!:(value:WebhookSigningKeyRotationReceipt)=>void;vi.mocked(api.prepareWebhookSigningKeyRotation).mockImplementationOnce(()=>new Promise(resolve=>{release=resolve;}));const context=await setup();state().controls.input('new-independent-secret');state().controls.prepare();await flushPromises();context.session.currentUser={...context.session.currentUser!,id:next,sessionId:next};release(prepared);await flushPromises();expect(state().secret).toBe('');expect(state().receipt).toBeUndefined();expect(state().unknown).toBe(false);
});
it('写后跨企业或错误回执保留未知结果，不将协议拒绝当作未提交',async()=>{
 vi.mocked(api.prepareWebhookSigningKeyRotation).mockImplementation(async(_id,request)=>({...prepared,operationId:request.operationId,tenantId:next}));await setup();state().controls.input('new-independent-secret');state().controls.prepare();await flushPromises();expect(state().unknown).toBe(true);expect(state().receipt).toBeUndefined();expect(state().secret).toBe('');expect(wrapper.emitted('unknown')?.at(-1)).toEqual([true]);
});
