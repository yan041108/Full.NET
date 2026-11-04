import { expect, it } from 'vitest';
import { readWorkflowReassignmentCandidatePageResponse as read } from '../src/workflow-reassignment-candidates';
const id = '01912345-6789-7abc-8def-0123456789aa';
const page = { instanceId: id, instanceRevision: 3, todoId: id, todoRevision: 7, items: [], page: 1, pageSize: 20, total: 0 };
it('规范化后端 int64 修订和总数，不改变资源绑定', () => {
  expect(read({ ...page, instanceRevision: '3', todoRevision: '7', total: '0' })).toEqual(page);
});
it.each([0, -1, 1.5, 'invalid', '9007199254740992', undefined])('拒绝不安全的待办修订 %s', todoRevision => {
  expect(() => read({ ...page, todoRevision })).toThrow();
});
it('拒绝无效资源和无待办资源', () => {
  expect(() => read({ ...page, instanceId: 'invalid' })).toThrow();
  expect(() => read({ ...page, todoId: '00000000-0000-0000-0000-000000000000' })).toThrow();
});
