import { mount, flushPromises, DOMWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ElInput, ElSelect, vLoading } from 'element-plus';
import PrintingTenantGrantsDialog from './PrintingTenantGrantsDialog.vue';
import ArtFormDialog from '../../framework/art-design/components/ArtFormDialog.vue';
import { useSessionStore } from '../../auth/session';
import { listPrintingTemplateVersions, listPrintingTenantVersionGrants, setPrintingTenantVersionGrant } from '../../api/printing-templates';
import { showSuccess, showProblem, showWarning } from '../../feedback/fullNetMessage';

vi.mock('../../api/printing-templates', () => ({listPrintingTemplateVersions:vi.fn(),listPrintingTenantVersionGrants:vi.fn(),setPrintingTenantVersionGrant:vi.fn()}));
vi.mock('../../feedback/fullNetMessage', () => ({showSuccess:vi.fn(),showProblem:vi.fn(),showWarning:vi.fn()}));
const body = new DOMWrapper(document.body);
const mounted: ReturnType<typeof mount>[] = [];
afterEach(() => { for (const wrapper of mounted.splice(0)) wrapper.unmount(); });
const template = {id:'019bc2b1-2a40-7cc3-8992-a80de51bf291',name:'Fixture',latestPublishedVersionNumber:2};
const tenant = '019bc2b1-2a40-7cc3-8992-a80de51bf292';
const versions = vi.mocked(listPrintingTemplateVersions); const grants = vi.mocked(listPrintingTenantVersionGrants); const setGrant = vi.mocked(setPrintingTenantVersionGrant);
function deferred<T>() { let resolve!: (v:T)=>void; let reject!: (e:unknown)=>void; const promise = new Promise<T>((r,j)=>{resolve=r;reject=j;});return {promise,resolve,reject}; }
function create(permissions = ['printing.templates.read','printing.templates.grant_tenants'], scope = 'host') {
 const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore(); session.state='authenticated';
 session.currentUser={id:tenant,username:'host',displayName:'Host',tenantId:scope==='host'?null:tenant,scope:scope as 'host',actorScope:scope as 'host',
  isSuperAdministrator:false,passwordChangeRequired:false,permissions,sessionId:template.id,preferredLocale:'zh-CN',profileVersion:1};
 // 使用真实 Teleport，避免弹窗与选择器嵌套传送门的替身引起递归更新。
 const wrapper=mount(PrintingTenantGrantsDialog,{props:{template},global:{plugins:[pinia],directives:{loading:vLoading},stubs:{Teleport:false}}});
 mounted.push(wrapper);return {session,wrapper};
}
async function enter(wrapper:ReturnType<typeof create>['wrapper'], value=tenant) { wrapper.getComponent(ElInput).vm.$emit('update:modelValue',value); await flushPromises(); }
describe('PrintingTenantGrantsDialog', () => {
 beforeEach(() => {vi.clearAllMocks();versions.mockResolvedValue([1,2].map(n=>({id:tenant.slice(0,-1)+String(n),templateId:template.id,versionNumber:n,
   layoutHtml:'<div>Frozen</div>',changeNote:null,publishedByUserId:tenant,publishedAtUtc:'2026-10-08T00:00:00Z'})));
  grants.mockResolvedValue({items:[tenant],page:1,pageSize:20,total:1});setGrant.mockResolvedValue(true); });
 it('loads the exact latest published version and grants only that frozen version', async () => {
  const {wrapper}=create();await flushPromises();expect(grants).toHaveBeenCalledWith(template.id,2,1,20,expect.any(AbortSignal));
  await enter(wrapper);await body.get('[data-testid="printing-grant-save"]').trigger('click');await flushPromises();
  expect(setGrant).toHaveBeenCalledWith(template.id,2,tenant,true,expect.any(AbortSignal));expect(showSuccess).toHaveBeenCalledOnce();wrapper.unmount();
 });
 it('changes versions with a fresh page and clears the previous grant rows', async () => {
  const {wrapper}=create();await flushPromises();const next=deferred<{items:string[];page:number;pageSize:number;total:number}>();grants.mockReturnValueOnce(next.promise);
  wrapper.getComponent(ElSelect).vm.$emit('update:modelValue',1);wrapper.getComponent(ElSelect).vm.$emit('change',1);await flushPromises();
  expect(body.find('[data-testid="printing-grant-revoke"]').exists()).toBe(false);expect(grants).toHaveBeenLastCalledWith(template.id,1,1,20,expect.any(AbortSignal));
  next.resolve({items:[],page:1,pageSize:20,total:0});await flushPromises();wrapper.unmount();
 });
 it('confirms revocation for the selected version and refreshes grants', async () => {
  const {wrapper}=create();await flushPromises();await body.get('[data-testid="printing-grant-revoke"]').trigger('click');await flushPromises();
  expect(setGrant).not.toHaveBeenCalled();await body.get('[data-testid=printing-grant-confirm-revoke]').trigger('click');await flushPromises();expect(setGrant).toHaveBeenCalledWith(template.id,2,tenant,false,expect.any(AbortSignal));wrapper.unmount();
 });
 it.each(['bad-id','00000000-0000-0000-0000-000000000000'])('rejects invalid or empty tenant IDs: %s', async value => {
  const {wrapper}=create();await flushPromises();await enter(wrapper,value);await body.get('[data-testid="printing-grant-save"]').trigger('click');await flushPromises();
  expect(setGrant).not.toHaveBeenCalled();expect(showWarning).toHaveBeenCalledOnce();wrapper.unmount();
 });
 it('does not request Host grants from a Tenant context or without the exact grant permission', async () => {
  for (const [permissions,scope] of [[['printing.templates.read'],'host'],[['printing.templates.read','printing.templates.grant_tenants'],'tenant']] as const) {
   const {wrapper}=create([...permissions],scope);await flushPromises();expect(grants).not.toHaveBeenCalled();expect(versions).not.toHaveBeenCalled();wrapper.unmount();
  }
 });
 it('aborts a closed mutation and ignores late success and refresh', async () => {
  const pending=deferred<boolean>();setGrant.mockReturnValueOnce(pending.promise);const {wrapper}=create();await flushPromises();await enter(wrapper);
  await body.get('[data-testid="printing-grant-save"]').trigger('click');await flushPromises();const signal=setGrant.mock.calls[0]![4]!;
  wrapper.getComponent(ArtFormDialog).vm.$emit('update:open',false);expect(signal.aborted).toBe(true);pending.resolve(true);await flushPromises();
  expect(wrapper.emitted('close')).toBeTruthy();expect(showSuccess).not.toHaveBeenCalled();expect(grants).toHaveBeenCalledTimes(1);wrapper.unmount();
 });
 it('clears sensitive rows and ignores late read errors after revocation', async () => {
  const pending=deferred<{items:string[];page:number;pageSize:number;total:number}>();grants.mockReturnValueOnce(pending.promise);const {wrapper,session}=create();await flushPromises();
  const signal=grants.mock.calls[0]![4]!;session.currentUser={...session.currentUser!,permissions:['printing.templates.read']};expect(signal.aborted).toBe(true);
  pending.reject(new Error('late'));await flushPromises();expect(showProblem).not.toHaveBeenCalled();expect(wrapper.emitted('close')).toBeTruthy();wrapper.unmount();
 });
 it('does not revoke after the session changes during the confirmation', async () => {
  const {wrapper,session}=create();await flushPromises();await body.get('[data-testid="printing-grant-revoke"]').trigger('click');await flushPromises();
  session.currentUser={...session.currentUser!,sessionId:tenant};await flushPromises();expect(setGrant).not.toHaveBeenCalled();wrapper.unmount();
 });
 it('owns the confirmation and clears its tenant text when the session changes', async () => {
  const {wrapper,session}=create();await flushPromises();await body.get('[data-testid="printing-grant-revoke"]').trigger('click');await flushPromises();
  expect(body.find('[data-testid="printing-grant-confirm-revoke"]').exists()).toBe(true);
  session.currentUser={...session.currentUser!,sessionId:tenant};await flushPromises();
  expect(body.find('[data-testid="printing-grant-confirm-revoke"]').exists()).toBe(false);
  expect(setGrant).not.toHaveBeenCalled();wrapper.unmount();
 });
 it('allows a failed grant to retry without broadening its version', async () => {
  setGrant.mockRejectedValueOnce({status:403,code:'authorization.permission_denied',title:'Denied'});
  const {wrapper}=create();await flushPromises();await enter(wrapper);await body.get('[data-testid="printing-grant-save"]').trigger('click');await flushPromises();
  expect(showProblem).toHaveBeenCalledOnce();await body.get('[data-testid="printing-grant-save"]').trigger('click');await flushPromises();expect(setGrant).toHaveBeenCalledTimes(2);wrapper.unmount();
 });
});
