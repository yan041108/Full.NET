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
