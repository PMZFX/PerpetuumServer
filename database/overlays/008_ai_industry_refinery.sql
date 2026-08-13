SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.ai_industry_goal', N'refinery_facility_eid') IS NULL
BEGIN
    EXEC(N'ALTER TABLE dbo.ai_industry_goal
        ADD refinery_facility_eid BIGINT NOT NULL
            CONSTRAINT DF_ai_industry_goal_refinery_facility DEFAULT (0) WITH VALUES;');
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.ai_industry_goal')
      AND name = N'CK_ai_industry_goal_refinery_facility'
)
BEGIN
    EXEC(N'ALTER TABLE dbo.ai_industry_goal
        ADD CONSTRAINT CK_ai_industry_goal_refinery_facility
            CHECK (refinery_facility_eid >= 0);');
END;

COMMIT TRANSACTION;
GO
