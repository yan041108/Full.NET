import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { describe, it } from 'node:test';

async function readDart(path) {
  return await readFile(new URL(path, import.meta.url), 'utf8');
}

describe('flutter workflow client contract', () => {
  it('pins Flutter SDK and avoids third-party UI frameworks', async () => {
    const [flutterVersion, pubspec] = await Promise.all([
      readFile(new URL('../.flutter-version', import.meta.url), 'utf8'),
      readDart('../pubspec.yaml'),
    ]);

    assert.equal(flutterVersion.trim(), '3.44.0');
    assert.match(pubspec, /http:\s*1\.2\.2/);
    assert.doesNotMatch(pubspec, /getwidget|flutter_screenutil|bruno/i);
  });

  it('keeps access tokens in memory and checks workflow permissions before actions', async () => {
    const [session, detailPage, todosPage] = await Promise.all([
      readDart('../lib/core/auth/identity_session.dart'),
      readDart('../lib/features/workflow/workflow_todo_detail_page.dart'),
      readDart('../lib/features/workflow/workflow_todos_page.dart'),
    ]);

    assert.doesNotMatch(session, /shared_preferences|secure_storage/i);
    assert.match(todosPage, /workflow\.todos\.read/);
    assert.match(detailPage, /workflow\.todos\.approve/);
    assert.match(detailPage, /workflow\.todos\.reject/);
    assert.match(detailPage, /createIdempotencyKey\(\)/);
  });

  it('consumes workflow OpenAPI paths for list, runtime and actions', async () => {
    const client = await readDart('../lib/features/workflow/workflow_todo_client.dart');

    assert.match(client, /\/api\/v1\/workflow\/todos\/mine/);
    assert.match(client, /\/runtime/);
    assert.match(client, /\/api\/v1\/workflow\/todos\/\$todoId\/\$action/);
    assert.match(client, /'approve'/);
    assert.match(client, /'reject'/);
  });

  it('limits the first slice to workflow todos without admin shell pages', async () => {
    const app = await readDart('../lib/app/full_net_app.dart');

    assert.match(app, /WorkflowTodosPage/);
    assert.match(app, /LoginPage/);
    assert.doesNotMatch(app, /notifications|inbox|admin|crud/i);
    assert.doesNotMatch(app, /MaterialApp\([\s\S]*routes:/);
  });
});
