import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ImportTaskCreateDialog from './ImportTaskCreateDialog.vue';
import * as api from '../api/import-export-tasks';
import { showProblem, showSuccess } from '../feedback/fullNetMessage';
import { createOutputSession, deferred } from '../test/data-output-fixtures';
import { importTask } from '../test/task-lifecycle-fixtures';

vi.mock('../api/import-export-tasks',()=>({listStaticImportSchemas:vi.fn(),createImportExportTask:vi.fn(),downloadStaticImportTemplate:vi.fn()}));
vi.mock('../feedback/fullNetMessage',()=>({showProblem:vi.fn(),showSuccess:vi.fn(),showWarning:vi.fn()}));
const schema={schemaKey:importTask.schemaKey,displayName:'租户职位',scopeKey:'tenant',requiredPermission:'organization.positions.import',
 worksheets:[{worksheetKey:'positions',displayName:'职位',headerColumns:['Code','Name']}]};
let wrapper:VueWrapper<InstanceType<typeof ImportTaskCreateDialog>>|undefined;
beforeEach(()=>{
 vi.clearAllMocks();for(const operation of Object.values(api))if(vi.isMockFunction(operation))operation.mockReset();
 vi.mocked(api.listStaticImportSchemas).mockResolvedValue([schema]);vi.mocked(api.createImportExportTask).mockResolvedValue(importTask);
});
afterEach(()=>{wrapper?.unmount();wrapper=undefined;vi.restoreAllMocks();});
async function setup(){
 const {pinia}=createOutputSession(['import_export.import_tasks.create','import_export.static_schemas.read',schema.requiredPermission]);
 wrapper=mount(ImportTaskCreateDialog,{props:{open:true},global:{plugins:[pinia],stubs:{
  ElDialog:{props:['modelValue'],template:'<div v-if="modelValue"><slot/><slot name="footer"/></div>'}
 }}});await flushPromises();
}
function close(){wrapper!.getComponent(ArtFormDialog).vm.$emit('update:open',false);}
async function upload(){
 const input=wrapper!.get('[data-testid="import-create-file"]');
 Object.defineProperty(input.element,'files',{value:[new File(['fixture'],'positions.xlsx')],configurable:true});await input.trigger('change');
 await wrapper!.get('[data-testid="import-create-submit"]').trigger('click');
}

describe('导入创建弹窗独立关闭边界',()=>{
 it('父组件尚未更新open时，关闭意图立即取消目录读取',async()=>{
  const pending=deferred<typeof schema[]>();vi.mocked(api.listStaticImportSchemas).mockReturnValueOnce(pending.promise);
  await setup();close();expect(vi.mocked(api.listStaticImportSchemas).mock.calls[0]![0]!.aborted).toBe(true);
  expect(wrapper!.props('open')).toBe(true);pending.resolve([schema]);await flushPromises();
  expect(wrapper!.get('[data-testid="import-create-schema"]').text()).not.toContain('租户职位');
 });
 it.each(['resolve','reject'] as const)('关闭后忽略上传%s，不发送created或反馈',async outcome=>{
  const pending=deferred<typeof importTask>();vi.mocked(api.createImportExportTask).mockReturnValueOnce(pending.promise);
  await setup();await upload();close();expect(vi.mocked(api.createImportExportTask).mock.calls[0]![3]!.aborted).toBe(true);
  if(outcome==='resolve')pending.resolve(importTask);else pending.reject(new Error('late upload'));
  await flushPromises();expect(wrapper!.emitted('created')).toBeUndefined();expect(showSuccess).not.toHaveBeenCalled();expect(showProblem).not.toHaveBeenCalled();
 });
 it.each(['resolve','reject'] as const)('关闭后忽略模板%s，不创建下载URL或反馈',async outcome=>{
  const pending=deferred<Blob>();vi.mocked(api.downloadStaticImportTemplate).mockReturnValueOnce(pending.promise);
  const createUrl=vi.spyOn(URL,'createObjectURL').mockReturnValue('blob:fixture');
  await setup();await wrapper!.get('[data-testid="import-create-template"]').trigger('click');close();
  expect(vi.mocked(api.downloadStaticImportTemplate).mock.calls[0]![2]!.aborted).toBe(true);
  if(outcome==='resolve')pending.resolve(new Blob(['fixture']));else pending.reject(new Error('late template'));
  await flushPromises();expect(createUrl).not.toHaveBeenCalled();expect(showProblem).not.toHaveBeenCalled();
 });
 it('父组件确认关闭并重新打开后仍能加载目录和提交',async()=>{
  await setup();await upload();await flushPromises();
  await wrapper!.setProps({open:false});await wrapper!.setProps({open:true});await flushPromises();
  await upload();await flushPromises();expect(api.listStaticImportSchemas).toHaveBeenCalledTimes(2);
  expect(api.createImportExportTask).toHaveBeenCalledTimes(2);expect(wrapper!.emitted('created')).toHaveLength(2);
 });
});
