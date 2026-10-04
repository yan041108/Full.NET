-- 247：只追加批次操作身份；不保存正文、凭据或向量，外键仅指向 AI 自有任务。
IF OBJECT_ID(N'dbo.fn_ai_knowledge_index_batch', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_ai_knowledge_index_batch (
        Id uniqueidentifier NOT NULL,
        IndexTaskId uniqueidentifier NOT NULL,
        BatchOrdinal int NOT NULL,
        RequestHash char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        InputCount int NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_ai_knowledge_index_batch PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_fn_ai_knowledge_index_batch_IndexTaskId FOREIGN KEY (IndexTaskId) REFERENCES dbo.fn_ai_knowledge_index_task (Id),
        CONSTRAINT CK_fn_ai_knowledge_index_batch_Bounds CHECK (BatchOrdinal >= 0 AND BatchOrdinal <= 4095 AND InputCount >= 1 AND InputCount <= 16)
    );
END;
-- 唯一索引可独立恢复；已有冲突行时停止，不删除或改写已记账的操作身份。
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_ai_knowledge_index_batch') AND name = N'UX_fn_ai_knowledge_index_batch_IndexTaskId_BatchOrdinal')
    CREATE UNIQUE INDEX UX_fn_ai_knowledge_index_batch_IndexTaskId_BatchOrdinal ON dbo.fn_ai_knowledge_index_batch (IndexTaskId, BatchOrdinal);
GO
-- 表及列注释逐项探测，允许创建完成但 DbUp 未记账时继续收敛。
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_batch') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'知识库索引批次与稳定预算操作身份', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_index_batch';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_batch') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_index_batch'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键及预算操作标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_index_batch', @level2type=N'COLUMN', @level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_batch') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_index_batch'),N'IndexTaskId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属索引任务代次', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_index_batch', @level2type=N'COLUMN', @level2name=N'IndexTaskId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_batch') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_index_batch'),N'BatchOrdinal','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'任务内零起始批次号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_index_batch', @level2type=N'COLUMN', @level2name=N'BatchOrdinal';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_batch') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_index_batch'),N'RequestHash','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'完整预算请求指纹', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_index_batch', @level2type=N'COLUMN', @level2name=N'RequestHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_batch') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_index_batch'),N'InputCount','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'批次输入数量', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_index_batch', @level2type=N'COLUMN', @level2name=N'InputCount';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_batch') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_index_batch'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间（UTC）', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_index_batch', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
GO
