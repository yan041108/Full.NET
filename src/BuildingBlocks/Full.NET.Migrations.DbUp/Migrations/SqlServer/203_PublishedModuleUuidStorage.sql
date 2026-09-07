-- 203：SQL Server 已使用 uniqueidentifier；仅验证配对迁移的存储不变量，不转换数据。

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_document_version_deletion_audit')
    AND name = N'Id' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_document_version_deletion_audit.Id', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_document_version_deletion_audit')
    AND name = N'DocumentItemId' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_document_version_deletion_audit.DocumentItemId', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_document_version_deletion_audit')
    AND name = N'VersionId' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_document_version_deletion_audit.VersionId', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_document_version_deletion_audit')
    AND name = N'FileId' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_document_version_deletion_audit.FileId', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_document_version_deletion_audit')
    AND name = N'UploadedByUserId' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_document_version_deletion_audit.UploadedByUserId', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_document_version_deletion_audit')
    AND name = N'DeletedByUserId' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_document_version_deletion_audit.DeletedByUserId', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_document_access_log')
    AND name = N'Id' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_document_access_log.Id', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_document_access_log')
    AND name = N'DocumentItemId' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_document_access_log.DocumentItemId', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_document_access_log')
    AND name = N'ActorUserId' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_document_access_log.ActorUserId', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_document_preview_task')
    AND name = N'Id' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_document_preview_task.Id', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_document_preview_task')
    AND name = N'DocumentItemId' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_document_preview_task.DocumentItemId', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_document_preview_task')
    AND name = N'VersionId' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_document_preview_task.VersionId', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_document_preview_task')
    AND name = N'SourceFileId' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_document_preview_task.SourceFileId', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_document_preview_task')
    AND name = N'OutputFileId' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_document_preview_task.OutputFileId', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_document_preview_task')
    AND name = N'RequestedByUserId' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_document_preview_task.RequestedByUserId', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_import_export_task')
    AND name = N'Id' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_import_export_task.Id', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_import_export_task')
    AND name = N'TenantId' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_import_export_task.TenantId', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_import_export_task')
    AND name = N'SourceFileId' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_import_export_task.SourceFileId', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_import_export_task')
    AND name = N'RequestedByUserId' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_import_export_task.RequestedByUserId', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_import_export_task')
    AND name = N'ErrorReceiptFileId' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_import_export_task.ErrorReceiptFileId', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_reporting_data_source')
    AND name = N'Id' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_reporting_data_source.Id', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_reporting_data_source')
    AND name = N'TenantId' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_reporting_data_source.TenantId', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_reporting_export_task')
    AND name = N'Id' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_reporting_export_task.Id', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_reporting_export_task')
    AND name = N'TenantId' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_reporting_export_task.TenantId', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_reporting_export_task')
    AND name = N'DefinitionId' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_reporting_export_task.DefinitionId', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_reporting_export_task')
    AND name = N'OutputFileId' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_reporting_export_task.OutputFileId', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_reporting_export_task')
    AND name = N'RequestedByUserId' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_reporting_export_task.RequestedByUserId', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_printing_template')
    AND name = N'Id' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_printing_template.Id', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_printing_template_version')
    AND name = N'Id' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_printing_template_version.Id', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_printing_template_version')
    AND name = N'TemplateId' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_printing_template_version.TemplateId', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_printing_template_version')
    AND name = N'PublishedByUserId' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_printing_template_version.PublishedByUserId', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_ai_model_config')
    AND name = N'Id' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_ai_model_config.Id', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_ai_model_config')
    AND name = N'TenantId' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_ai_model_config.TenantId', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_ai_tenant_quota')
    AND name = N'Id' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_ai_tenant_quota.Id', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_ai_tenant_quota')
    AND name = N'TenantId' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_ai_tenant_quota.TenantId', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_ai_chat_session')
    AND name = N'Id' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_ai_chat_session.Id', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_ai_chat_session')
    AND name = N'TenantId' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_ai_chat_session.TenantId', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_ai_chat_session')
    AND name = N'OwnerUserId' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_ai_chat_session.OwnerUserId', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_ai_chat_session')
    AND name = N'ModelConfigId' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_ai_chat_session.ModelConfigId', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_ai_chat_message')
    AND name = N'Id' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_ai_chat_message.Id', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_ai_chat_message')
    AND name = N'SessionId' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_ai_chat_message.SessionId', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_ai_agent_tool_call')
    AND name = N'Id' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_ai_agent_tool_call.Id', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_ai_agent_tool_call')
    AND name = N'TenantId' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_ai_agent_tool_call.TenantId', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_ai_agent_tool_call')
    AND name = N'ActorUserId' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_ai_agent_tool_call.ActorUserId', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_payment_merchant_config')
    AND name = N'Id' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_payment_merchant_config.Id', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_payment_merchant_config')
    AND name = N'TenantId' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_payment_merchant_config.TenantId', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_payment_order')
    AND name = N'Id' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_payment_order.Id', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_payment_order')
    AND name = N'TenantId' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_payment_order.TenantId', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_payment_order')
    AND name = N'MerchantConfigId' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_payment_order.MerchantConfigId', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_payment_notify_receipt')
    AND name = N'Id' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_payment_notify_receipt.Id', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_payment_notify_receipt')
    AND name = N'MerchantConfigId' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_payment_notify_receipt.MerchantConfigId', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_payment_refund')
    AND name = N'Id' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_payment_refund.Id', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_payment_refund')
    AND name = N'TenantId' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_payment_refund.TenantId', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_payment_refund')
    AND name = N'OrderId' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_payment_refund.OrderId', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_payment_refund')
    AND name = N'MerchantConfigId' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_payment_refund.MerchantConfigId', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_goview_project')
    AND name = N'Id' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_goview_project.Id', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_goview_project_version')
    AND name = N'Id' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_goview_project_version.Id', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_goview_project_version')
    AND name = N'ProjectId' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_goview_project_version.ProjectId', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_goview_project_version')
    AND name = N'PublishedByUserId' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_goview_project_version.PublishedByUserId', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_k3cloud_connection_config')
    AND name = N'Id' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_k3cloud_connection_config.Id', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_k3cloud_document_sync')
    AND name = N'Id' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_k3cloud_document_sync.Id', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_k3cloud_document_sync')
    AND name = N'ConnectionConfigId' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_k3cloud_document_sync.ConnectionConfigId', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_k3cloud_document_sync')
    AND name = N'CreatedByUserId' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_k3cloud_document_sync.CreatedByUserId', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_ocr_provider_config')
    AND name = N'Id' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_ocr_provider_config.Id', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_ocr_id_card_task')
    AND name = N'Id' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_ocr_id_card_task.Id', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_ocr_id_card_task')
    AND name = N'SourceFileId' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_ocr_id_card_task.SourceFileId', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_ocr_id_card_task')
    AND name = N'ConfirmedByUserId' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_ocr_id_card_task.ConfirmedByUserId', 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.fn_ocr_id_card_task')
    AND name = N'CreatedByUserId' AND system_type_id = TYPE_ID(N'uniqueidentifier'))
    THROW 51203, 'UUID storage invariant failed: fn_ocr_id_card_task.CreatedByUserId', 1;
