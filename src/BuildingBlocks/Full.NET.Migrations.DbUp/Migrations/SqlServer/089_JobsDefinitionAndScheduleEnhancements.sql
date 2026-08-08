-- 089：对齐 Admin.NET 任务管理能力，为作业定义补充分组，为任务计划补充运行统计、时间窗口与参数列。
-- 所有新增列均允许为空或带默认值，保证存量行迁移后立即可用，不破坏既有 TriggerShape 约束。

-- 作业定义：作业分组，对应 Admin.NET SysJobDetail.GroupName，用于按组筛选与展示。
IF COL_LENGTH(N'dbo.fn_jobs_definition', N'GroupName') IS NULL
BEGIN
    ALTER TABLE dbo.fn_jobs_definition
        ADD GroupName nvarchar(64) NULL;
END;

-- 任务计划：触发次数，计划每次实际创建执行记录时由调度器递增，对应 Admin.NET SysJobTrigger.NumberOfRuns。
IF COL_LENGTH(N'dbo.fn_jobs_schedule', N'NumberOfRuns') IS NULL
BEGIN
    ALTER TABLE dbo.fn_jobs_schedule
        ADD NumberOfRuns bigint NOT NULL
            CONSTRAINT DF_fn_jobs_schedule_NumberOfRuns DEFAULT (0);
END;

-- 任务计划：出错次数，执行记录终态为 failed 时由执行器递增，对应 Admin.NET SysJobTrigger.NumberOfErrors。
IF COL_LENGTH(N'dbo.fn_jobs_schedule', N'NumberOfErrors') IS NULL
BEGIN
    ALTER TABLE dbo.fn_jobs_schedule
        ADD NumberOfErrors bigint NOT NULL
            CONSTRAINT DF_fn_jobs_schedule_NumberOfErrors DEFAULT (0);
END;

-- 任务计划：生效起始时刻（UTC），超过该时刻才允许触发，对应 Admin.NET SysJobTrigger.StartTime。
IF COL_LENGTH(N'dbo.fn_jobs_schedule', N'StartTime') IS NULL
BEGIN
    ALTER TABLE dbo.fn_jobs_schedule
        ADD StartTime datetimeoffset(7) NULL;
END;

-- 任务计划：失效结束时刻（UTC），超过该时刻计划标记完成，对应 Admin.NET SysJobTrigger.EndTime。
IF COL_LENGTH(N'dbo.fn_jobs_schedule', N'EndTime') IS NULL
BEGIN
    ALTER TABLE dbo.fn_jobs_schedule
        ADD EndTime datetimeoffset(7) NULL;
END;

-- 任务计划：触发器参数，存储传给作业处理器的参数文本，对应 Admin.NET SysJobTrigger.Args。
IF COL_LENGTH(N'dbo.fn_jobs_schedule', N'Args') IS NULL
BEGIN
    ALTER TABLE dbo.fn_jobs_schedule
        ADD Args nvarchar(500) NULL;
END;
