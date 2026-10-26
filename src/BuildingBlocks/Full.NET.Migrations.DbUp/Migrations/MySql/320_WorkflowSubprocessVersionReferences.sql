-- 320：只回填真实同作用域固定引用；隐式提交DDL逐对象探测恢复。
CREATE TABLE IF NOT EXISTS fn_workflow_subprocess_ref (
 ParentVersionId binary(16) NOT NULL COMMENT '拥有子调用的固定父版本',
 ChildVersionId binary(16) NOT NULL COMMENT '被父版本固定引用的子版本',
 TenantScopeKey varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '双方定义共有的可信作用域',
 CreatedAtUtc datetime(6) NOT NULL COMMENT '父版本发布时刻；统一UTC微秒精度',
 CONSTRAINT pk_fn_workflow_subprocess_ref PRIMARY KEY (ParentVersionId,ChildVersionId)
) COMMENT='工作流已发布父子版本依赖；固定子版本删除由同模块外键保护' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
DROP PROCEDURE IF EXISTS fn_workflow_subprocess_ref_ensure;
DELIMITER $$
CREATE PROCEDURE fn_workflow_subprocess_ref_ensure()
BEGIN
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_ref' AND INDEX_NAME='ix_fn_workflow_subprocess_ref_child') THEN
  CREATE INDEX ix_fn_workflow_subprocess_ref_child ON fn_workflow_subprocess_ref(ChildVersionId,TenantScopeKey,ParentVersionId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_ref' AND CONSTRAINT_NAME='fk_fn_workflow_subprocess_ref_parent') THEN
  ALTER TABLE fn_workflow_subprocess_ref ADD CONSTRAINT fk_fn_workflow_subprocess_ref_parent FOREIGN KEY (ParentVersionId) REFERENCES fn_workflow_definition_version(Id);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_ref' AND CONSTRAINT_NAME='fk_fn_workflow_subprocess_ref_child') THEN
  ALTER TABLE fn_workflow_subprocess_ref ADD CONSTRAINT fk_fn_workflow_subprocess_ref_child FOREIGN KEY (ChildVersionId) REFERENCES fn_workflow_definition_version(Id);
 END IF;
 IF EXISTS (SELECT 1 FROM fn_workflow_definition_version AS parentVersion
 INNER JOIN fn_workflow_definition AS parentDefinition ON parentDefinition.Id=parentVersion.DefinitionId
 INNER JOIN JSON_TABLE(parentVersion.CanonicalJson,'$.nodes[*]' COLUMNS (NodeTypeKey varchar(64) PATH '$.nodeTypeKey' NULL ON EMPTY ERROR ON ERROR, ChildVersionText varchar(64) PATH '$.config.subprocessPolicy.definitionVersionId' NULL ON EMPTY ERROR ON ERROR)) AS node ON TRUE
 LEFT JOIN fn_workflow_definition_version AS childVersion ON childVersion.Id=CASE WHEN IS_UUID(node.ChildVersionText) THEN UUID_TO_BIN(node.ChildVersionText,0) ELSE NULL END
 LEFT JOIN fn_workflow_definition AS childDefinition ON childDefinition.Id=childVersion.DefinitionId
 WHERE node.NodeTypeKey='process.subprocess' AND (childVersion.Id IS NULL OR childVersion.Id=parentVersion.Id OR childDefinition.TenantScopeKey<>parentDefinition.TenantScopeKey)) THEN
  SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Workflow subprocess version dependency is missing or crosses scope.';
 END IF;
 INSERT INTO fn_workflow_subprocess_ref (ParentVersionId,ChildVersionId,TenantScopeKey,CreatedAtUtc)
 SELECT DISTINCT parentVersion.Id,childVersion.Id,parentDefinition.TenantScopeKey,parentVersion.PublishedAtUtc
 FROM fn_workflow_definition_version AS parentVersion
 INNER JOIN fn_workflow_definition AS parentDefinition ON parentDefinition.Id=parentVersion.DefinitionId
 INNER JOIN JSON_TABLE(parentVersion.CanonicalJson,'$.nodes[*]' COLUMNS (NodeTypeKey varchar(64) PATH '$.nodeTypeKey' NULL ON EMPTY ERROR ON ERROR, ChildVersionText varchar(64) PATH '$.config.subprocessPolicy.definitionVersionId' NULL ON EMPTY ERROR ON ERROR)) AS node ON TRUE
 LEFT JOIN fn_workflow_definition_version AS childVersion ON childVersion.Id=CASE WHEN IS_UUID(node.ChildVersionText) THEN UUID_TO_BIN(node.ChildVersionText,0) ELSE NULL END
 LEFT JOIN fn_workflow_definition AS childDefinition ON childDefinition.Id=childVersion.DefinitionId
 WHERE node.NodeTypeKey='process.subprocess' AND NOT EXISTS (SELECT 1 FROM fn_workflow_subprocess_ref AS existing WHERE existing.ParentVersionId=parentVersion.Id AND existing.ChildVersionId=childVersion.Id);
END$$
DELIMITER ;
CALL fn_workflow_subprocess_ref_ensure();
DROP PROCEDURE IF EXISTS fn_workflow_subprocess_ref_ensure;
