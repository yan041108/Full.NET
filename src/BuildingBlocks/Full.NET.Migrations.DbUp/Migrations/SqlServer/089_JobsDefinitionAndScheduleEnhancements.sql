-- 089：对齐 Admin.NET 任务管理能力，为作业定义补充分组，为任务计划补充运行统计、时间窗口与参数列。
-- 所有新增列均允许为空或带默认值，保证存量行迁移后立即可用，不破坏既有 TriggerShape 约束。

-- 作业定义：作业分组，对应 Admin.NET SysJobDetail.GroupName，用于按组筛选与展示。
IF COL_LENGTH(N'dbo.fn_jobs_definition', N'GroupName') IS NULL
BEGIN
    ALTER TABLE dbo.fn_jobs_definition
        ADD GroupName nvarchar(64) NULL;
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_jobs_definition')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_jobs_definition'), N'GroupName', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'分组名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_definition', @level2type=N'COLUMN', @level2name=N'GroupName';
END;

-- 任务计划：触发次数，计划每次实际创建执行记录时由调度器递增，对应 Admin.NET SysJobTrigger.NumberOfRuns。
IF COL_LENGTH(N'dbo.fn_jobs_schedule', N'NumberOfRuns') IS NULL
BEGIN
    ALTER TABLE dbo.fn_jobs_schedule
        ADD NumberOfRuns bigint NOT NULL
            CONSTRAINT DF_fn_jobs_schedule_NumberOfRuns DEFAULT (0);
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_jobs_schedule')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_jobs_schedule'), N'NumberOfRuns', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'运行次数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_schedule', @level2type=N'COLUMN', @level2name=N'NumberOfRuns';
END;

-- 任务计划：出错次数，执行记录终态为 failed 时由执行器递增，对应 Admin.NET SysJobTrigger.NumberOfErrors。
IF COL_LENGTH(N'dbo.fn_jobs_schedule', N'NumberOfErrors') IS NULL
BEGIN
    ALTER TABLE dbo.fn_jobs_schedule
        ADD NumberOfErrors bigint NOT NULL
            CONSTRAINT DF_fn_jobs_schedule_NumberOfErrors DEFAULT (0);
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_jobs_schedule')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_jobs_schedule'), N'NumberOfErrors', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'错误次数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_schedule', @level2type=N'COLUMN', @level2name=N'NumberOfErrors';
END;

-- 任务计划：生效起始时刻（UTC），超过该时刻才允许触发，对应 Admin.NET SysJobTrigger.StartTime。
IF COL_LENGTH(N'dbo.fn_jobs_schedule', N'StartTime') IS NULL
BEGIN
    ALTER TABLE dbo.fn_jobs_schedule
        ADD StartTime datetimeoffset(7) NULL;
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_jobs_schedule')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_jobs_schedule'), N'StartTime', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Start Time', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_schedule', @level2type=N'COLUMN', @level2name=N'StartTime';
END;

-- 任务计划：失效结束时刻（UTC），超过该时刻计划标记完成，对应 Admin.NET SysJobTrigger.EndTime。
IF COL_LENGTH(N'dbo.fn_jobs_schedule', N'EndTime') IS NULL
BEGIN
    ALTER TABLE dbo.fn_jobs_schedule
        ADD EndTime datetimeoffset(7) NULL;
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_jobs_schedule')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_jobs_schedule'), N'EndTime', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'结束时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_schedule', @level2type=N'COLUMN', @level2name=N'EndTime';
END;

-- 任务计划：触发器参数，存储传给作业处理器的参数文本，对应 Admin.NET SysJobTrigger.Args。
IF COL_LENGTH(N'dbo.fn_jobs_schedule', N'Args') IS NULL
BEGIN
    ALTER TABLE dbo.fn_jobs_schedule
        ADD Args nvarchar(500) NULL;
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_jobs_schedule')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_jobs_schedule'), N'Args', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'调度参数(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_schedule', @level2type=N'COLUMN', @level2name=N'Args';
END;
