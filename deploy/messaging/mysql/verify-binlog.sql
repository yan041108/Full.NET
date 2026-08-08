-- Verify MySQL binlog prerequisites before registering the Debezium connector.
-- Expected: log_bin=ON, binlog_format=ROW, binlog_row_image=FULL

SHOW VARIABLES
WHERE Variable_name IN ('log_bin', 'binlog_format', 'binlog_row_image', 'server_id', 'binlog_expire_logs_seconds');

-- Manual acceptance:
-- log_bin = ON
-- binlog_format = ROW
-- binlog_row_image = FULL
-- server_id unique in the cluster and distinct from Debezium database.server.id
-- binlog_expire_logs_seconds retention exceeds maximum connector outage window