-- 201：新增完整载荷摘要。历史未保存正文，不能伪造回填摘要；旧幂等键重放将失败关闭。
IF COL_LENGTH(N'dbo.fn_mqtt_message', N'PayloadDigest') IS NULL
    ALTER TABLE dbo.fn_mqtt_message ADD PayloadDigest varchar(64) NULL;

IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE major_id = OBJECT_ID(N'dbo.fn_mqtt_message')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_mqtt_message'), N'PayloadDigest', 'ColumnId')
      AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',
        @value=N'原 UTF-8 正文 SHA-256 摘要；历史记录为空',
        @level0type=N'SCHEMA', @level0name=N'dbo',
        @level1type=N'TABLE', @level1name=N'fn_mqtt_message',
        @level2type=N'COLUMN', @level2name=N'PayloadDigest';
