-- 私人反馈来源使用闭合不可变协议；已删除旧约束但未记账的执行可重新收敛。
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_faq') AND name=N'CK_fn_ai_knowledge_faq_Bounds' AND definition NOT LIKE '%feedback_derived%')
    ALTER TABLE fn_ai_knowledge_faq DROP CONSTRAINT CK_fn_ai_knowledge_faq_Bounds;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_ai_knowledge_faq') AND name=N'CK_fn_ai_knowledge_faq_Bounds')
    ALTER TABLE fn_ai_knowledge_faq WITH CHECK ADD CONSTRAINT CK_fn_ai_knowledge_faq_Bounds CHECK (HeadVersion>0 AND ((OriginKey='independent' AND Basis IS NOT NULL) OR (OriginKey IN ('document_derived','faq_derived','feedback_derived') AND Basis IS NULL)));
IF EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=1 AND major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_faq') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_faq'),N'OriginKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_updateextendedproperty @name=N'MS_Description',@value=N'independent、document_derived、faq_derived 或 feedback_derived，来源不可修改',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_faq',@level2type=N'COLUMN',@level2name=N'OriginKey';
