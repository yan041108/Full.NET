-- 205：生成槽位持久化所有权、到期回收和远程取消；发布时停止旧 API 实例。

IF COL_LENGTH(N'dbo.fn_ai_chat_session', N'GenerationId') IS NULL
    ALTER TABLE dbo.fn_ai_chat_session ADD GenerationId uniqueidentifier NULL;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_ai_chat_session')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_chat_session'), N'GenerationId', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'生成代次标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_chat_session', @level2type=N'COLUMN', @level2name=N'GenerationId';

IF NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_chat_session') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_chat_session'), N'GenerationId', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'当前生成所有权标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_chat_session', @level2type=N'COLUMN', @level2name=N'GenerationId';

IF COL_LENGTH(N'dbo.fn_ai_chat_session', N'GenerationExpiresAtUtc') IS NULL
    ALTER TABLE dbo.fn_ai_chat_session ADD GenerationExpiresAtUtc datetimeoffset(7) NULL;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_ai_chat_session')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_chat_session'), N'GenerationExpiresAtUtc', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Generation Expires At(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_chat_session', @level2type=N'COLUMN', @level2name=N'GenerationExpiresAtUtc';

IF NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_chat_session') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_chat_session'), N'GenerationExpiresAtUtc', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'生成租约到期时间 UTC', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_chat_session', @level2type=N'COLUMN', @level2name=N'GenerationExpiresAtUtc';

IF COL_LENGTH(N'dbo.fn_ai_chat_session', N'GenerationCancellationRequested') IS NULL
    ALTER TABLE dbo.fn_ai_chat_session ADD GenerationCancellationRequested bit NOT NULL DEFAULT 0;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_ai_chat_session')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_chat_session'), N'GenerationCancellationRequested', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否已请求取消生成', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_chat_session', @level2type=N'COLUMN', @level2name=N'GenerationCancellationRequested';

IF NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_chat_session') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_chat_session'), N'GenerationCancellationRequested', 'ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'跨实例取消请求标志', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_chat_session', @level2type=N'COLUMN', @level2name=N'GenerationCancellationRequested';

-- 只释放旧版本留下且没有生成标识的状态；重跑时保留所有新版有效租约。
EXEC(N'UPDATE fn_ai_chat_message SET StatusKey = ''failed'' WHERE StatusKey = ''streaming'' AND EXISTS (SELECT 1 FROM fn_ai_chat_session WHERE fn_ai_chat_session.Id = fn_ai_chat_message.SessionId AND GenerationId IS NULL AND IsGenerating = 1);');
EXEC(N'UPDATE fn_ai_chat_session SET IsGenerating = 0 WHERE IsGenerating = 1 AND GenerationId IS NULL;');
