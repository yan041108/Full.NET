-- 237：清除历史驱动错误中可能包含的连接信息。
IF OBJECT_ID(N'dbo.fn_reporting_data_source', N'U') IS NULL
    THROW 51237, 'Reporting data source table is missing.', 1;

UPDATE dbo.fn_reporting_data_source
SET LastTestMessage = N'Reporting data source test failed.'
WHERE LastTestStatusKey = N'failed'
  AND LastTestMessage IS NOT NULL;
