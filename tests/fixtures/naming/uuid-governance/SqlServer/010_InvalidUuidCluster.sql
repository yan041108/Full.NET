-- 010+ SQL Server 治理夹具：UUID 主键未显式声明聚集属性。
CREATE TABLE dbo.fn_sample_module_widget
(
    Id uniqueidentifier NOT NULL,
    CONSTRAINT PK_fn_sample_module_widget PRIMARY KEY (Id)
);

CREATE TABLE dbo.fn_outbox_message
(
    Id uniqueidentifier NOT NULL,
    OccurredAt datetime2(6) NOT NULL,
    CONSTRAINT PK_fn_outbox_message PRIMARY KEY (Id)
);
