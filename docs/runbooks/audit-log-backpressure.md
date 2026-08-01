# Runbook: Audit and log backpressure

## Reliability classes

| Class | Path | Backpressure behavior |
| --- | --- | --- |
| B0 Domain Audit | Same DB transaction as business | Never degrade to Best Effort or Outbox |
| B1 Important HTTP Audit | Bounded micro-batch, request waits for attempt | Default fail-open with alerts; critical actions must be B0 |
| B2 HTTP Operation Log | Bounded async Priority/Best Effort streams | May sample/drop by policy |

## B1 pressure

1. Alert on queue depth, wait P99, and failures (`FullNetAuditB1*`).
2. Do not route B1 through Outbox or Fluent Bit “Durable” duplication.
3. If fail-open is dropping audits, page product owner for actions that must become B0.
4. Scale Worker/DB only after confirming micro-batch coordinator health.

## B2 / Fluent Bit pressure

1. Priority and Best Effort have separate buffers/routes.
2. On Spool high/disk full: shed Best Effort first; protect Priority.
3. Cold archive B2 to S3; never write collector failures back into the same tail path.

## Collector interruption

Application keeps writing Compact JSON stdout. Restore Fluent Bit / OTel queues from `file_storage` without creating a second Durable Audit pipeline.
