import assert from 'node:assert/strict';
import test from 'node:test';
import { createStackResourceScope, releaseOwnedStack } from './stack-resource-scope.mjs';

test('partial startup resources are disposed in reverse order exactly once', async () => {
  const scope = createStackResourceScope();
  const stopped = [];
  scope.add(async () => stopped.push('database'));
  scope.add(async () => stopped.push('redis'));
  await scope.dispose(); await scope.dispose();
  assert.deepEqual(stopped, ['redis', 'database']);
  assert.throws(() => scope.add(async () => {}), /already disposed/);
});

test('one failed stop does not strand other resources and all failures remain visible', async () => {
  const scope = createStackResourceScope();
  const stopped = [];
  const databaseError = new Error('database stop');
  const apiError = new Error('API stop');
  scope.add(async () => { stopped.push('database'); throw databaseError; });
  scope.add(async () => stopped.push('redis'));
  scope.add(async () => { stopped.push('api'); throw apiError; });
  await assert.rejects(scope.dispose(), error => {
    assert.deepEqual(stopped, ['api', 'redis', 'database']);
    assert.ok(error instanceof AggregateError);
    assert.deepEqual(error.errors, [apiError, databaseError]);
    return true;
  });
});

test('ownership metadata is cleared only after resources have stopped', async () => {
  const scope = createStackResourceScope();
  const events = [];
  scope.add(async () => events.push('stopped'));
  await releaseOwnedStack(scope, () => events.push('state cleared'));
  assert.deepEqual(events, ['stopped', 'state cleared']);
});

test('failed shutdown retains ownership metadata for surviving resources', async () => {
  const scope = createStackResourceScope();
  let cleared = false;
  scope.add(async () => { throw new Error('still running'); });
  await assert.rejects(releaseOwnedStack(scope, () => { cleared = true; }), AggregateError);
  assert.equal(cleared, false);
});
