-- 231：文档标签热门/推荐运营标记。

IF COL_LENGTH(N'dbo.fn_document_tag', N'IsHot') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_tag
        ADD IsHot bit NOT NULL
            CONSTRAINT DF_fn_document_tag_IsHot DEFAULT (0);
END;

IF COL_LENGTH(N'dbo.fn_document_tag', N'IsRecommended') IS NULL
BEGIN
    ALTER TABLE dbo.fn_document_tag
        ADD IsRecommended bit NOT NULL
            CONSTRAINT DF_fn_document_tag_IsRecommended DEFAULT (0);
END;

IF COL_LENGTH(N'dbo.fn_document_tag', N'IsHot') IS NOT NULL
   AND NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_tag')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_tag'), N'IsHot', 'ColumnId')
          AND name = N'MS_Description'
    )
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否Hot', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_tag', @level2type=N'COLUMN', @level2name=N'IsHot';

IF COL_LENGTH(N'dbo.fn_document_tag', N'IsRecommended') IS NOT NULL
   AND NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_document_tag')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_document_tag'), N'IsRecommended', 'ColumnId')
          AND name = N'MS_Description'
    )
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否Recommended', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_tag', @level2type=N'COLUMN', @level2name=N'IsRecommended';
