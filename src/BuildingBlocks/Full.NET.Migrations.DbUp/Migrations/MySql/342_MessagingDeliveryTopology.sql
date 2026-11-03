-- 342：仅增加可空元数据列；DDL 部分完成、DbUp 未记账时可直接重跑，不推断或回填旧拓扑。
DROP PROCEDURE IF EXISTS fn_messaging_delivery_topology_ensure;
DELIMITER $$
CREATE PROCEDURE fn_messaging_delivery_topology_ensure()
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema=DATABASE()
        AND table_name='fn_messaging_stream_ownership' AND column_name='CdcKafkaBindingJson') THEN
        ALTER TABLE fn_messaging_stream_ownership ADD COLUMN CdcKafkaBindingJson LONGTEXT NULL
            COMMENT '切流时冻结的 Kafka 资源身份与原窗口 JSON；缺失时禁止自动回退';
    END IF;
END$$
DELIMITER ;
CALL fn_messaging_delivery_topology_ensure();
DROP PROCEDURE IF EXISTS fn_messaging_delivery_topology_ensure;
