-- 固定 DDL 与精确结构探测；DROP CHECK 已提交但 ADD 尚未完成时可重复收敛。
DROP PROCEDURE IF EXISTS fn_ai_knowledge_faq_ensure_parents;
DELIMITER $$
CREATE PROCEDURE fn_ai_knowledge_faq_ensure_parents()
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS AS constraints_row INNER JOIN information_schema.CHECK_CONSTRAINTS AS checks_row ON checks_row.CONSTRAINT_SCHEMA=constraints_row.CONSTRAINT_SCHEMA AND checks_row.CONSTRAINT_NAME=constraints_row.CONSTRAINT_NAME WHERE constraints_row.CONSTRAINT_SCHEMA=DATABASE() AND constraints_row.TABLE_NAME='fn_ai_knowledge_faq' AND constraints_row.CONSTRAINT_NAME='CK_fn_ai_knowledge_faq_Bounds' AND checks_row.CHECK_CLAUSE NOT LIKE '%faq_derived%') THEN
        ALTER TABLE fn_ai_knowledge_faq DROP CHECK CK_fn_ai_knowledge_faq_Bounds;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_faq' AND CONSTRAINT_NAME='CK_fn_ai_knowledge_faq_Bounds') THEN
        ALTER TABLE fn_ai_knowledge_faq ADD CONSTRAINT CK_fn_ai_knowledge_faq_Bounds CHECK (HeadVersion>0 AND ((OriginKey='independent' AND Basis IS NOT NULL) OR (OriginKey IN ('document_derived','faq_derived') AND Basis IS NULL)));
    END IF;
END$$
DELIMITER ;
CALL fn_ai_knowledge_faq_ensure_parents();
DROP PROCEDURE IF EXISTS fn_ai_knowledge_faq_ensure_parents;
ALTER TABLE fn_ai_knowledge_faq MODIFY COLUMN OriginKey varchar(32) NOT NULL COMMENT 'independent、document_derived 或 faq_derived，来源不可修改';
