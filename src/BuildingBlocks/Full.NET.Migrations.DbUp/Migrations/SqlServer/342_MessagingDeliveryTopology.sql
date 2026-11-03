-- 342：CDC 切流时冻结不可变 Kafka 拓扑；旧记录不按当前资源回填，缺基线保持失败关闭。
IF COL_LENGTH(N'dbo.fn_messaging_stream_ownership', N'CdcKafkaBindingJson') IS NULL
    ALTER TABLE dbo.fn_messaging_stream_ownership ADD CdcKafkaBindingJson nvarchar(max) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_messaging_stream_ownership')
    AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_messaging_stream_ownership'),N'CdcKafkaBindingJson','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'切流时冻结的 Kafka 资源身份与原窗口 JSON；缺失时禁止自动回退',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_messaging_stream_ownership',
        @level2type=N'COLUMN', @level2name=N'CdcKafkaBindingJson';
