import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const root = fileURLToPath(new URL('../..', import.meta.url));

function runBlockers(scenario, provider) {
  const script = String.raw`
$scenario = $env:FULLNET_CUTOVER_TEST_CASE
function Probe([string]$query) {
    if ($query -notmatch 'blocking_resources AS' -or $query -notmatch 'TOP \(2\)|LIMIT 2' -or $query -notmatch 'b.HasUnboundReadyFile') { throw 'Unexpected blocker query.' }
    if ($scenario -in @('half-cursor', 'empty-cursor')) { throw 'Database must not be called.' }
    $global:LASTEXITCODE = 0
    $first = '00000000-0000-7000-8000-00000000000a'
    $second = '00000000-0000-7000-8000-00000000000b'
    $tab = [char]9
    switch ($scenario) {
        'empty' { return }
        'readonly' { '-1' }
        'invalid' { $first, 'bad', '1010' -join $tab }
        'missing-reason' { $first, $second -join $tab }
        'invalid-reason' { $first, $second, '1200' -join $tab }
        'empty-reason' { $first, $second, '0000' -join $tab }
        'duplicate' { $first, $second, '0010' -join $tab; $first.ToUpperInvariant(), $second, '0001' -join $tab }
        'multi-reason' { $first, $second, '1111' -join $tab }
        'next-page' {
            if ($query -notmatch 'b.TenantId > ' -or $query -notmatch 'b.TenantId = .+ AND b.ResourceId > ' -or $query -notlike "*$first*") { throw 'Missing cursor predicate.' }
            $first, $second, '0100' -join $tab
        }
        default { $first, $first, '1000' -join $tab; $first, $second, '0011' -join $tab }
    }
}
function sqlcmd { Probe $args[-1] }
function mysql { Probe $args[-1] }
$parameters = @{ Provider=$env:FULLNET_CUTOVER_TEST_PROVIDER;Server='fixture.invalid';Database='fixture';Limit=1;MySqlLoginPath='fixture-reader';MySqlCaFile='./eng/deploy/reporting-cutover/mysql.sql' }
if ($scenario -in @('next-page', 'half-cursor')) { $parameters.AfterTenantId = '00000000-0000-7000-8000-00000000000a' }
if ($scenario -eq 'next-page') { $parameters.AfterTaskId = $parameters.AfterTenantId }
if ($scenario -eq 'empty-cursor') { $parameters.AfterTenantId = [guid]::Empty; $parameters.AfterTaskId = [guid]::Empty }
& './eng/deploy/reporting-cutover/Get-ReportingCutoverBlockers.ps1' @parameters
`;
  const result = spawnSync('pwsh', ['-NoProfile', '-NonInteractive', '-Command', script], {
    cwd: root, encoding: 'utf8', timeout: 20_000, windowsHide: true,
    env: { ...process.env, FULLNET_CUTOVER_TEST_CASE: scenario, FULLNET_CUTOVER_TEST_PROVIDER: provider }
  });
  assert.ifError(result.error);
  return result;
}

for (const provider of ['SqlServer', 'MySql']) {
  test(`${provider} blocker lookup continues after a tenant and task cursor`, () => {
    const { status, stdout, stderr } = runBlockers('next-page', provider);
    assert.equal(status, 0, stderr);
    assert.deepEqual(JSON.parse(stdout), { hasMore: false, items: [{
      tenantId: '00000000-0000-7000-8000-00000000000a', taskId: '00000000-0000-7000-8000-00000000000b',
      reasons: ['task_has_lease']
    }] });
  });

  test(`${provider} blocker lookup returns at most the requested limit`, () => {
    const { status, stdout, stderr } = runBlockers('truncated', provider);
    assert.equal(status, 0, stderr);
    assert.deepEqual(JSON.parse(stdout), { hasMore: true, items: [{
      tenantId: '00000000-0000-7000-8000-00000000000a', taskId: '00000000-0000-7000-8000-00000000000a',
      reasons: ['task_unfinished']
    }] });
  });

  test(`${provider} blocker lookup returns every applicable reason for one resource`, () => {
    const { status, stdout, stderr } = runBlockers('multi-reason', provider);
    assert.equal(status, 0, stderr);
    assert.deepEqual(JSON.parse(stdout).items[0].reasons,
      ['task_unfinished', 'task_has_lease', 'pending_upload', 'ready_file_unbound']);
  });
}

for (const scenario of ['half-cursor', 'empty-cursor']) {
  test(`${scenario} blocker lookup rejects an invalid cursor before connecting`, () => {
    const { status, stdout, stderr } = runBlockers(scenario, 'SqlServer');
    assert.notEqual(status, 0);
    assert.equal(stdout.trim(), '');
    assert.match(stderr, /must be supplied together as non-empty UUIDs/);
  });
}

test('an empty blocker lookup returns an empty array', () => {
  const { status, stdout, stderr } = runBlockers('empty', 'SqlServer');
  assert.equal(status, 0, stderr);
  assert.deepEqual(JSON.parse(stdout), { hasMore: false, items: [] });
});

for (const scenario of ['readonly', 'invalid', 'missing-reason', 'invalid-reason', 'empty-reason', 'duplicate']) {
  test(`${scenario} blocker lookup fails without emitting records`, () => {
    const { status, stdout, stderr } = runBlockers(scenario, 'MySql');
    assert.notEqual(status, 0);
    assert.equal(stdout.trim(), '');
    assert.match(stderr, scenario === 'readonly' ? /writable primary/ : /Invalid reporting cutover blocker result/);
  });
}

function runEvidence(scenario, provider = 'SqlServer') {
  const script = String.raw`
$tenant = '00000000-0000-7000-8000-000000000001'
$task = '00000000-0000-7000-8000-000000000002'
$scenario = $env:FULLNET_CUTOVER_TEST_CASE
function Reply {
    if ($scenario -eq 'invalid-id') { throw 'Database must not be called.' }
    if ($args[0] -notlike "*$tenant*" -or $args[0] -notlike "*$task*" -or $args[0] -like '*{{*') { throw 'UUID bindings missing.' }
    $global:LASTEXITCODE = 0
    switch ($scenario) {
        'readonly' { '-1' }
        'missing' { 'null' }
        'malformed' { 'malformed-result' }
        'wrong-tenant' { @{tenantId=$task;taskId=$task} | ConvertTo-Json -Compress }
        'orphan' { @{tenantId=$tenant;taskId=$task;taskExists=0;statusKey=$null;hasLease=0;pendingFileCount=1;readyFileCount=1;unresolvedFileCount=2} | ConvertTo-Json -Compress }
        default { @{tenantId=$tenant;taskId=$task;taskExists=1;hasLease=1;hasRowIdentityPrefix=0;fileCount=2;unresolvedFileCount=1} | ConvertTo-Json -Compress }
    }
}

function sqlcmd {
    if ('-N' -notin $args -or '-C' -in $args -or '-x' -notin $args -or '-Q' -notin $args) { throw 'Unexpected SQL Server transport.' }
    Reply $args[-1]
}
function mysql {
    if ('--ssl-mode=VERIFY_IDENTITY' -notin $args -or '--raw' -notin $args) { throw 'Unexpected MySQL transport.' }
    Reply $args[-1]
}
function helm { throw 'Evidence must not call Helm.' }
$parameters = @{Provider=$env:FULLNET_CUTOVER_TEST_PROVIDER;Server='fixture.invalid';Database='fixture';TenantId=$tenant;TaskId=$task;
    MySqlLoginPath='fixture-reader';MySqlCaFile='./eng/deploy/reporting-cutover/mysql.sql'}
if ($scenario -eq 'invalid-id') { $parameters.TaskId = 'invalid-id' }
& './eng/deploy/reporting-cutover/Get-ReportingCutoverEvidence.ps1' @parameters
`;
  const result = spawnSync('pwsh', ['-NoProfile', '-NonInteractive', '-Command', script], {
    cwd: root, encoding: 'utf8', timeout: 20_000, windowsHide: true,
    env: { ...process.env, FULLNET_CUTOVER_TEST_CASE: scenario, FULLNET_CUTOVER_TEST_PROVIDER: provider }
  });
  assert.ifError(result.error);
  return result;
}

for (const provider of ['SqlServer', 'MySql']) {
  test(`${provider} evidence returns the requested task without invoking deployment`, () => {
    const { status, stdout, stderr } = runEvidence('ready', provider);
    assert.equal(status, 0, stderr);
    const record = JSON.parse(stdout);
    assert.equal(record.fileCount, 2);
    assert.equal(record.taskExists, true);
    assert.equal(record.unresolvedFileCount, 1);
    assert.equal(record.hasLease, true);
    assert.equal(record.hasRowIdentityPrefix, false);
  });
  test(`${provider} evidence preserves orphan file blockers without an export task`, () => {
    const { status, stdout, stderr } = runEvidence('orphan', provider);
    assert.equal(status, 0, stderr);
    const record = JSON.parse(stdout);
    assert.equal(record.taskExists, false);
    assert.equal(record.statusKey, null);
    assert.equal(record.hasLease, false);
    assert.equal(record.unresolvedFileCount, 2);
  });

  test(`${provider} evidence rejects read-only database state`, () => {
    const { status, stdout, stderr } = runEvidence('readonly', provider);
    assert.notEqual(status, 0);
    assert.equal(stdout.trim(), '');
    assert.match(stderr, /writable primary/);
  });
}

for (const [scenario, message] of [
  ['missing', /No reporting task or owned files found/],
  ['malformed', /Invalid reporting cutover evidence result/],
  ['wrong-tenant', /Invalid reporting cutover evidence result/],
  ['invalid-id', /Cannot process argument transformation on parameter 'TaskId'/],
]) {
  test(`${scenario} evidence fails without returning a record`, () => {
    const { status, stdout, stderr } = runEvidence(scenario);
    assert.notEqual(status, 0);
    assert.equal(stdout.trim(), '');
    assert.match(stderr, message);
    assert.doesNotMatch(stderr, /Database must not be called|malformed-result/);
  });
}

function runFileCandidates(scenario, provider) {
  const script = String.raw`
$tenant = '00000000-0000-7000-8000-000000000001'
$task = '00000000-0000-7000-8000-000000000002'
$fileA = '00000000-0000-7000-8000-00000000000a'
$fileB = '00000000-0000-7000-8000-00000000000b'
$scenario = $env:FULLNET_CUTOVER_TEST_CASE
function ReadCandidates([string] $Query) {
    if ($scenario -eq 'empty-cursor') { throw 'Database must not be called.' }
    if ($Query -notlike "*$tenant*" -or $Query -notlike "*$task*" -or $Query -like '*{{*' -or
        $Query -notmatch 'f.TenantId = @TenantId AND f.ResourceId = @TaskId' -or
        $Query -notmatch "f.OwnerModuleKey = 'reporting'" -or $Query -notmatch 'TOP \(2\)|LIMIT 2') {
        throw 'Unexpected candidate query.'
    }
    $global:LASTEXITCODE = 0
    $first = $fileA, 'ready', '1200', ('A' * 64), '2026-11-17T00:00:00Z', 1 -join [char]9
    $second = $fileB, 'pending', '3400', ('b' * 64), '2026-11-17T00:00:01.123456Z', 0 -join [char]9
    switch ($scenario) {
        'empty' { return }
        'readonly' { '-1' }
        'malformed' { $first; $second.Replace(('b' * 64), 'not-a-hash') }
        'duplicate' { $first; $first.ToUpperInvariant() }
        'next' {
            if ($Query -notlike "*$fileA*" -or $Query -notmatch 'f.Id > @AfterFileId') { throw 'Missing file cursor.' }
            $second
        }
        default { $first; $second }
    }
}
function sqlcmd {
    if ('-N' -notin $args -or '-C' -in $args -or '-x' -notin $args) { throw 'Unexpected SQL Server transport.' }
    ReadCandidates $args[-1]
}
function mysql {
    if ('--ssl-mode=VERIFY_IDENTITY' -notin $args -or '--raw' -notin $args) { throw 'Unexpected MySQL transport.' }
    ReadCandidates $args[-1]
}
$parameters = @{Provider=$env:FULLNET_CUTOVER_TEST_PROVIDER;Server='fixture.invalid';Database='fixture';TenantId=$tenant;TaskId=$task;Limit=1;
    MySqlLoginPath='fixture-reader';MySqlCaFile='./eng/deploy/reporting-cutover/mysql.sql'}
if ($scenario -eq 'next') { $parameters.AfterFileId = $fileA }
if ($scenario -eq 'empty-cursor') { $parameters.AfterFileId = [guid]::Empty }
& './eng/deploy/reporting-cutover/Get-ReportingCutoverFiles.ps1' @parameters
`;
  const result = spawnSync('pwsh', ['-NoProfile', '-NonInteractive', '-Command', script], {
    cwd: root, encoding: 'utf8', timeout: 20_000, windowsHide: true,
    env: { ...process.env, FULLNET_CUTOVER_TEST_CASE: scenario, FULLNET_CUTOVER_TEST_PROVIDER: provider }
  });
  assert.ifError(result.error);
  return result;
}

for (const provider of ['SqlServer', 'MySql']) {
  test(`${provider} file candidates return bounded data and continue from the last file`, () => {
    const first = runFileCandidates('first', provider);
    assert.equal(first.status, 0, first.stderr);
    assert.deepEqual(JSON.parse(first.stdout), {
      tenantId: '00000000-0000-7000-8000-000000000001',
      taskId: '00000000-0000-7000-8000-000000000002',
      hasMore: true,
      items: [{
        fileId: '00000000-0000-7000-8000-00000000000a', statusKey: 'ready', sizeBytes: 1200,
        sha256: 'a'.repeat(64), createdAtUtc: '2026-11-17T00:00:00Z', isBound: true
      }]
    });
    const next = runFileCandidates('next', provider);
    assert.equal(next.status, 0, next.stderr);
    assert.deepEqual(JSON.parse(next.stdout).items, [{
      fileId: '00000000-0000-7000-8000-00000000000b', statusKey: 'pending', sizeBytes: 3400,
      sha256: 'b'.repeat(64), createdAtUtc: '2026-11-17T00:00:01.123456Z', isBound: false
    }]);
    assert.equal(JSON.parse(next.stdout).hasMore, false);
  });
}

test('a file candidate lookup with no active files returns an empty array', () => {
  const result = runFileCandidates('empty', 'SqlServer');
  assert.equal(result.status, 0, result.stderr);
  assert.deepEqual(JSON.parse(result.stdout).items, []);
  assert.equal(JSON.parse(result.stdout).hasMore, false);
});

for (const [scenario, error] of [
  ['readonly', /writable primary/], ['malformed', /Invalid reporting cutover file result/],
  ['duplicate', /Invalid reporting cutover file result/], ['empty-cursor', /AfterFileId must be a non-empty UUID/]
]) {
  test(`file candidate lookup rejects ${scenario} without emitting a partial page`, () => {
    const { status, stdout, stderr } = runFileCandidates(scenario, 'MySql');
    assert.notEqual(status, 0);
    assert.equal(stdout.trim(), '');
    assert.match(stderr, error);
  });
}

function run(scenario, provider = 'SqlServer') {
  const script = String.raw`
$scenario = $env:FULLNET_CUTOVER_TEST_CASE
$provider = $env:FULLNET_CUTOVER_TEST_PROVIDER
$global:probeRan = $false
function kubectl {
    $global:LASTEXITCODE = 0
    if ($args[1] -ne 'isolated-test' -or $args[3] -ne 'fixture') { throw 'Unexpected Kubernetes target.' }
    $role = if ($args[-1] -like '*fullnet-worker') { 'worker' } else { 'api' }
    $items = @()
    switch ($args[6]) {
        'hpa' {
            if ($scenario -eq 'autoscaler') { $items = @(@{spec=@{scaleTargetRef=@{kind='Deployment';name='worker-fixture'}}}) }
        }
        'deployments' {
            $replicas = if ($role -eq 'api' -and ($scenario -eq 'active' -or ($scenario -eq 'restarted' -and $global:probeRan))) { 1 } else { 0 }
            $generation = if ($role -eq 'worker' -and $scenario -eq 'cycled-during-probe' -and $global:probeRan) { 4 } else { 2 }
            $uid = if ($role -eq 'api' -and $scenario -eq 'replaced-during-probe' -and $global:probeRan) { 'replacement-uid' } else { "$role-uid" }
            $observed = if ($scenario -eq 'stale') { 1 } else { $generation }
            $items = @(@{metadata=@{name="$role-fixture";uid=$uid;generation=$generation;labels=@{'app.kubernetes.io/component'=$role}};
                spec=@{replicas=$replicas};status=@{observedGeneration=$observed}})
        }
        'replicasets' {
            $replicas = if ($scenario -eq 'active-replicaset') { 1 } else { 0 }
            $items = @(@{metadata=@{uid='worker-rs';name='worker-fixture-hash';ownerReferences=@(@{controller=$true;kind='Deployment';uid='worker-uid'})};
                spec=@{replicas=$replicas};status=@{replicas=$replicas}})
        }
        'pods' {
            $items = @(@{metadata=@{labels=@{'app.kubernetes.io/instance'='unrelated'}}})
            if ($scenario -eq 'terminating') {
                $items += @{metadata=@{deletionTimestamp='2026-11-10T00:00:00Z';labels=@{'app.kubernetes.io/instance'='fullnet-worker'}}}
            }
            if ($scenario -eq 'relabeled-pod' -or ($scenario -eq 'relabeled-during-probe' -and $global:probeRan)) {
                $items += @{metadata=@{ownerReferences=@(@{controller=$true;kind='ReplicaSet';uid='worker-rs'});
                    labels=@{'app.kubernetes.io/instance'='different'}}}
            }
            if ($scenario -eq 'released-pod') {
                $items += @{metadata=@{name='worker-fixture-hash-abcde';labels=@{'app.kubernetes.io/instance'='different'}}}
            }
            if ($scenario -eq 'cluster-unavailable') { $global:LASTEXITCODE = 1; return }
        }
        default { throw 'Unexpected fixture command.' }
    }
    @{items=$items} | ConvertTo-Json -Compress -Depth 8
}
function Invoke-TestDatabaseProbe {
    $global:probeRan = $true
    Write-Host 'database-probed'
    $global:LASTEXITCODE = if ($scenario -eq 'db-error') { 1 } else { 0 }
    switch ($scenario) {
        'pending' { '2' }
        'readonly' { '-1' }
        'unexpected' { '0'; 'more' }
        default { '0' }
    }
}
function sqlcmd {
    if ('-N' -notin $args -or '-C' -in $args -or $args[-1] -notlike '*sqlserver.sql') { throw 'Unexpected SQL Server transport.' }
    Invoke-TestDatabaseProbe
}
function mysql {
    if ('--ssl-mode=VERIFY_IDENTITY' -notin $args -or '--login-path=fixture-reader' -notin $args) { throw 'Unexpected MySQL transport.' }
    Invoke-TestDatabaseProbe
}
function helm {
    if ($args[0] -eq 'template') {
        $release = $args[1]
        if ('--is-upgrade' -notin $args -or '--show-only' -notin $args -or 'templates/configmap.yaml' -notin $args) {
            throw 'Unexpected chart preview.'
        }
        if (($release -ceq 'fullnet-api') -ne ('api.reportingExportAdmissionEnabled=false' -in $args)) { throw 'Preview uses a different admission override.' }
        Write-Host "chart-preview=$release"
        if ($env:FULLNET_CUTOVER_REAL_HELM -and $scenario -in 'ready', 'provider-mismatch', 'cutover-role-secrets') {
            & $env:FULLNET_CUTOVER_REAL_HELM @args
            return
        }
        if ($scenario -eq 'render-failed' -and $release -eq 'fullnet-worker') {
            $global:LASTEXITCODE = 1
            'sensitive-render-diagnostic'
            return
        }
        $global:LASTEXITCODE = 0
        $chartProvider = if ($scenario -eq 'provider-mismatch' -and $release -eq 'fullnet-worker') { 'MySql' } else { $provider }
        'sensitive-render-diagnostic'
        '  Database__Provider: "{0}"' -f $chartProvider
        $secret = @($args | Where-Object { $_ -like 'database.connectionSecretName=*' })
        $secretName = if ($secret.Count -eq 1) { $secret[0].Split('=')[1] } else { 'fullnet-database' }
        if ($release -eq 'fullnet-worker' -and $scenario -eq 'secret-mismatch') { $secretName = 'fullnet-api.db' }
        '            - name: Database__ConnectionString'
        if ($release -eq 'fullnet-api' -and $scenario -eq 'connection-inline') {
            '              value: "fixture-private-value"'
        } else {
            '              valueFrom:'
            '                secretKeyRef:'
            '                  name: {0}' -f $secretName
            '                  key: connectionString'
        }
        if ($release -eq 'fullnet-api' -and $scenario -ne 'admission-missing') {
            '            - name: FullNet__Reporting__Export__AdmissionEnabled'
            if ($scenario -eq 'admission-enabled') { '              value: "true"' } else { '              value: "false"' }
            if ($scenario -eq 'admission-duplicate') {
                '            - name: FullNet__Reporting__Export__AdmissionEnabled'
                '              value: "true"'
            }
        }
        return
    }
    if ($args[-2] -ne '--kube-context' -or $args[-1] -ne 'isolated-test') { throw 'Unexpected Helm context.' }
    if ('--wait' -notin $args -or '--wait-for-jobs' -notin $args) { throw 'Missing Helm wait flags.' }
    $admission = @($args | Where-Object { $_ -like 'api.reportingExportAdmissionEnabled=*' })
    $expectedPause = $scenario -notin 'normal', 'role-secrets', 'invalid-role-secret' -and $args[2] -ceq 'fullnet-api'
    if ($expectedPause) {
        $index = [array]::IndexOf($args, 'api.reportingExportAdmissionEnabled=false')
        if ($admission.Count -ne 1 -or $index -le 0 -or $args[$index - 1] -cne '--set') {
            throw 'The cutover API must receive exactly one boolean admission pause.'
        }
        Write-Host 'reporting-admission=paused'
    } elseif ($admission.Count -ne 0) {
        throw 'Unexpected admission override for a non-cutover API stage.'
    }
    $secret = @($args | Where-Object { $_ -like 'database.connectionSecretName=*' })
    if ($scenario -in 'role-secrets', 'cutover-role-secrets', 'secret-mismatch') {
        if ($secret.Count -ne 1 -or $secret[0] -cne "database.connectionSecretName=$($args[2]).db" -or '--set-string' -notin $args) {
            throw 'The release did not receive its own Secret.'
        }
    } elseif ($secret.Count -ne 0) {
        throw 'Unspecified Secret override must use chart values.'
    }
    $global:LASTEXITCODE = if ($scenario -eq 'migrator-failed' -and $args[2] -eq 'fullnet-migrator') { 1 } else { 0 }
    Write-Host "helm-stage=$($args[2])"
}
$parameters = @{ Namespace='fixture'; ImageTag='fixed-test'; KubeContext='isolated-test' }
if ($scenario -in 'role-secrets', 'invalid-role-secret', 'cutover-role-secrets', 'secret-mismatch') {
    $parameters.ApiDatabaseSecretName = 'fullnet-api.db'
    $parameters.WorkerDatabaseSecretName = 'fullnet-worker.db'
    $parameters.MigratorDatabaseSecretName = 'fullnet-migrator.db'
    if ($scenario -eq 'invalid-role-secret') { $parameters.ApiDatabaseSecretName = 'db,image.tag=injected' }
}
if ($scenario -notin 'normal', 'role-secrets', 'invalid-role-secret') {
    $parameters.ReportingOutputCutover = $true
    $parameters.ReportingDatabaseProvider = $provider
    $parameters.ReportingDatabaseServer = 'db.example.invalid'
    $parameters.ReportingDatabaseName = 'fixture'
    if ($provider -eq 'MySql') {
        $parameters.ReportingMySqlLoginPath = 'fixture-reader'
        $parameters.ReportingMySqlCaFile = './eng/deploy/reporting-cutover/mysql.sql'
        $parameters.ProviderValuesFile = './deploy/helm/fullnet/ci/values-provider-mysql.yaml'
    }
    if ($env:FULLNET_CUTOVER_REAL_HELM -and $scenario -eq 'provider-mismatch') {
        $parameters.ProviderValuesFile = './deploy/helm/fullnet/ci/values-provider-mysql.yaml'
    }
    if ($scenario -eq 'missing-context') { $parameters.Remove('KubeContext') }
}
& './eng/deploy/Invoke-FullNetRelease.ps1' @parameters
`;
  const result = spawnSync('pwsh', ['-NoProfile', '-NonInteractive', '-Command', script], {
    cwd: root, encoding: 'utf8', timeout: 20_000, windowsHide: true,
    env: { ...process.env, FULLNET_CUTOVER_TEST_CASE: scenario, FULLNET_CUTOVER_TEST_PROVIDER: provider }
  });
  assert.ifError(result.error);
  return result;
}

for (const provider of ['SqlServer', 'MySql']) {
  test(`${provider} zero pending allows the exact three release stages and publishes the API paused`, () => {
    const { status, stdout, stderr } = run('ready', provider);
    assert.equal(status, 0, stderr);
    assert.match(stdout, /chart-preview=fullnet-migrator[\s\S]+chart-preview=fullnet-worker[\s\S]+chart-preview=fullnet-api[\s\S]+database-probed/);
    assert.match(stdout, /database-probed[\s\S]+preflight passed/);
    assert.deepEqual([...stdout.matchAll(/helm-stage=(fullnet-[a-z]+)/g)].map(match => match[1]),
      ['fullnet-migrator', 'fullnet-worker', 'fullnet-api']);
    assert.match(stdout, /helm-stage=fullnet-worker[\s\S]+reporting-admission=paused[\s\S]+helm-stage=fullnet-api/);
  });
}

for (const [scenario, message] of [
  ['render-failed', /could not render the chart for 'fullnet-worker'/],
  ['provider-mismatch', process.env.FULLNET_CUTOVER_REAL_HELM
    ? /provider does not match the probe for 'fullnet-migrator'/ : /provider does not match the probe for 'fullnet-worker'/],
  ['admission-missing', /exactly one disabled API export admission/],
  ['admission-enabled', /exactly one disabled API export admission/],
  ['admission-duplicate', /exactly one disabled API export admission/],
  ['connection-inline', /exactly one database connection Secret reference for 'fullnet-api'/],
  ['secret-mismatch', /does not use the requested database Secret for 'fullnet-worker'/],
]) {
  test(`${scenario} chart preview blocks the database probe and release stages`, () => {
    const { status, stdout, stderr } = run(scenario);
    assert.notEqual(status, 0);
    assert.match(stderr, message);
    assert.doesNotMatch(stdout + stderr, /helm-stage|database-probed|sensitive-render-diagnostic|fixture-private-value/);
  });
}

for (const [scenario, message, probesDatabase] of [
  ['missing-context', /explicit Kubernetes context/, false],
  ['active', /stopped and reconciled/, false],
  ['stale', /stopped and reconciled/, false],
  ['terminating', /pods to terminate/, false],
  ['relabeled-pod', /pods to terminate/, false],
  ['relabeled-during-probe', /pods to terminate/, true],
  ['cycled-during-probe', /worker deployment changed during the database check/, true],
  ['replaced-during-probe', /api deployment changed during the database check/, true],
  ['released-pod', /pods to terminate/, false],
  ['active-replicaset', /replica sets to stop/, false],
  ['autoscaler', /removing the autoscaler/, false],
  ['cluster-unavailable', /cannot read Kubernetes pods/, false],
  ['pending', /blocked by 2 export resources with unfinished tasks, leases or unresolved files/, true],
  ['readonly', /writable primary/, true],
  ['unexpected', /Invalid reporting cutover database result/, true],
  ['db-error', /could not read the export state/, true],
  ['restarted', /stopped and reconciled/, true]
]) {
  test(`${scenario} blocks all Helm mutations`, () => {
    const { status, stdout, stderr } = run(scenario);
    assert.notEqual(status, 0, stdout);
    assert.match(stderr, message);
    assert.doesNotMatch(stdout, /helm-stage/);
    assert.equal(stdout.includes('database-probed'), probesDatabase);
  });
}

test('ordinary release keeps the stage order without probing reporting', () => {
  const { status, stdout, stderr } = run('normal');
  assert.equal(status, 0, stderr);
  assert.doesNotMatch(stdout, /database-probed|chart-preview/);
  assert.match(stdout, /helm-stage=fullnet-migrator[\s\S]+helm-stage=fullnet-worker[\s\S]+helm-stage=fullnet-api/);
});

test('each Helm stage can select its database Secret and invalid values fail before mutation', () => {
  const accepted = run('role-secrets');
  assert.equal(accepted.status, 0, accepted.stderr);
  assert.match(accepted.stdout, /helm-stage=fullnet-migrator[\s\S]+helm-stage=fullnet-worker[\s\S]+helm-stage=fullnet-api/);
  const rejected = run('invalid-role-secret');
  assert.notEqual(rejected.status, 0);
  assert.match(rejected.stderr, /ApiDatabaseSecretName/);
  assert.doesNotMatch(rejected.stdout, /helm-stage|database-probed/);
});

test('cutover preview and actual Helm arguments preserve the three explicit role Secrets', () => {
  const { status, stdout, stderr } = run('cutover-role-secrets');
  assert.equal(status, 0, stderr);
  assert.match(stdout, /chart-preview=fullnet-migrator[\s\S]+chart-preview=fullnet-worker[\s\S]+chart-preview=fullnet-api[\s\S]+database-probed/);
  assert.deepEqual([...stdout.matchAll(/helm-stage=(fullnet-[a-z]+)/g)].map(match => match[1]),
    ['fullnet-migrator', 'fullnet-worker', 'fullnet-api']);
});

test('a failed Migrator stops the cutover before releasing Worker and API', () => {
  const { status, stdout, stderr } = run('migrator-failed');
  assert.notEqual(status, 0, stdout);
  assert.match(stderr, /Helm release 'fullnet-migrator' failed with exit code 1/);
  assert.deepEqual([...stdout.matchAll(/helm-stage=(fullnet-[a-z]+)/g)].map(match => match[1]),
    ['fullnet-migrator']);
});
