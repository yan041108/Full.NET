-- 254：为答案消息保存完整来源依赖和不可降级的派生标记。旧消息保持普通聊天语义。
-- SQL Server 以 UTF-16 字节数限制元数据；不删除历史数据。开放问答前必须停止不识别来源标记的旧 API。
IF COL_LENGTH(N'dbo.fn_ai_chat_message', N'IsKnowledgeAnswer') IS NULL
    ALTER TABLE dbo.fn_ai_chat_message ADD IsKnowledgeAnswer bit NOT NULL CONSTRAINT DF_fn_ai_chat_message_IsKnowledgeAnswer DEFAULT 0;
IF COL_LENGTH(N'dbo.fn_ai_chat_message', N'KnowledgeLineage') IS NULL
    ALTER TABLE dbo.fn_ai_chat_message ADD KnowledgeLineage nvarchar(max) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_chat_message') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_chat_message'),N'IsKnowledgeAnswer','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'是否携带知识来源依赖',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_chat_message',@level2type=N'COLUMN',@level2name=N'IsKnowledgeAnswer';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_chat_message') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_chat_message'),N'KnowledgeLineage','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'绑定答案消息及可信范围的版本化完整来源依赖',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_chat_message',@level2type=N'COLUMN',@level2name=N'KnowledgeLineage';

-- 独立标记允许空依赖占位；读取方必须失败关闭，不能把损坏的派生答案当作普通消息。
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_ai_chat_message') AND name=N'CK_fn_ai_chat_message_KnowledgeLineage')
    EXEC(N'ALTER TABLE dbo.fn_ai_chat_message WITH CHECK ADD CONSTRAINT CK_fn_ai_chat_message_KnowledgeLineage CHECK ((IsKnowledgeAnswer=0 AND KnowledgeLineage IS NULL) OR (IsKnowledgeAnswer=1 AND (KnowledgeLineage IS NULL OR DATALENGTH(KnowledgeLineage) BETWEEN 2 AND 32768)))');
ALTER TABLE dbo.fn_ai_chat_message WITH CHECK CHECK CONSTRAINT CK_fn_ai_chat_message_KnowledgeLineage;
