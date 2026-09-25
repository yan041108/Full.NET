-- 237：清除历史驱动错误中可能包含的连接信息。
UPDATE fn_reporting_data_source
SET LastTestMessage = 'Reporting data source test failed.'
WHERE LastTestStatusKey = 'failed'
  AND LastTestMessage IS NOT NULL;
