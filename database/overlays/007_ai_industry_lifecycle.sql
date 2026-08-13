SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.ai_industry_goal', N'research_facility_eid') IS NULL
BEGIN
    ALTER TABLE dbo.ai_industry_goal
        ADD research_facility_eid BIGINT NOT NULL
            CONSTRAINT DF_ai_industry_goal_research_facility DEFAULT (0) WITH VALUES;
END;

IF COL_LENGTH(N'dbo.ai_industry_goal', N'prototype_facility_eid') IS NULL
BEGIN
    ALTER TABLE dbo.ai_industry_goal
        ADD prototype_facility_eid BIGINT NOT NULL
            CONSTRAINT DF_ai_industry_goal_prototype_facility DEFAULT (0) WITH VALUES;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.ai_industry_goal')
      AND name = N'CK_ai_industry_goal_research_facility'
)
BEGIN
    ALTER TABLE dbo.ai_industry_goal
        ADD CONSTRAINT CK_ai_industry_goal_research_facility
            CHECK (research_facility_eid >= 0);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.ai_industry_goal')
      AND name = N'CK_ai_industry_goal_prototype_facility'
)
BEGIN
    ALTER TABLE dbo.ai_industry_goal
        ADD CONSTRAINT CK_ai_industry_goal_prototype_facility
            CHECK (prototype_facility_eid >= 0);
END;

COMMIT TRANSACTION;
GO
