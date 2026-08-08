# Full.NET Messaging CDC Shadow (Development / Test Only)

Kafka 4.1.2 and Debezium Connect overlays for **local development and integration testing only**. The application Helm chart (`deploy/helm/fullnet`) does **not** install Kafka, Debezium, or any CDC infrastructure.

## Scope

- Capture committed `INSERT` rows from append-only `fn_messaging_outbox_event` via SQL Server CDC or MySQL ROW Binlog.
- Route events to Shadow Topic prefix `fullnet.dev.shadow.*` using Debezium Outbox Event Router.
- Shadow Topics are for comparison evidence only; **no business Consumer** may subscribe to them.
- Heartbeat, Schema History, and Connect internal topics use `fullnet.dev.shadow.internal.*` and are likewise not business-facing.

## Pinned images (no `:latest`)

| Component | Image |
| --- | --- |
| Kafka broker | `apache/kafka:4.1.2` |
| Debezium Connect | `quay.io/debezium/connect:3.4.3.Final` |

Production must **not** run these public images directly. Production Connect must be built by the trusted platform from fixed Debezium Connector artifacts (or an approved supported distribution), with vulnerability scan, signing, SBOM, and digest pinning evidence on file.

## Quick start (dev)

```bash
export FULLNET_SQLSERVER_HOST=host.docker.internal
export FULLNET_SQLSERVER_PORT=1433
export FULLNET_SQLSERVER_USER=fullnet_cdc_reader
export FULLNET_SQLSERVER_PASSWORD=replace-me
export FULLNET_SQLSERVER_DATABASE=fullnet_dev

export FULLNET_MYSQL_HOST=host.docker.internal
export FULLNET_MYSQL_PORT=3306
export FULLNET_MYSQL_USER=fullnet_cdc_reader
export FULLNET_MYSQL_PASSWORD=replace-me
export FULLNET_MYSQL_DATABASE=fullnet_dev

docker compose -f deploy/messaging/compose.kafka-debezium.yml up -d
```

Run `deploy/messaging/sqlserver/enable-outbox-cdc.sql` (privileged DBA operation, **not DbUp**) and `deploy/messaging/mysql/verify-binlog.sql` before registering connectors.

## Secret injection

Compose and connector templates use placeholder environment variables. Inject secrets via shell env, gitignored `.env`, or your platform secret store. Never commit passwords or connection strings.

## SQL Server CDC (privileged operations)

SQL Server CDC is an **explicit privileged DBA operation**:

1. Database-level CDC and SQL Server Agent must be enabled by operations - **not** by DbUp migrations, API startup, or Worker bootstrap.
2. Table-level CDC for `dbo.fn_messaging_outbox_event` uses stable capture instance `fullnet_fn_messaging_outbox_event`.
3. To disable: run `sqlserver/disable-outbox-cdc.sql` during approved maintenance.

## MySQL Binlog prerequisites

- `log_bin=ON`
- `binlog_format=ROW`
- `binlog_row_image=FULL`

Use `mysql/verify-binlog.sql` to inspect current settings.

## Verification

```powershell
node --test tests/deployment/messaging-cdc-contract.test.mjs
pnpm test:helm
pnpm test:observability-deploy
```

## Production boundary

- `deploy/helm/fullnet` must not reference `quay.io/debezium/*` or ship Kafka/Debezium as application-owned stateful services.
- Shadow validation must complete before any formal business Consumer is enabled on CDC-delivered topics.