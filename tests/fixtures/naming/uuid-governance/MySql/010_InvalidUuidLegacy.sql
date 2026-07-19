-- 010+ MySQL 治理夹具：UUID 列仍使用 legacy char(36)。
CREATE TABLE fn_sample_module_widget
(
    Id char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    TenantId char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    CONSTRAINT PK_fn_sample_module_widget PRIMARY KEY (Id)
);
