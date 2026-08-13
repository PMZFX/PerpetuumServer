SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.ai_industry_goal', N'committed_demand_quantity') IS NULL
BEGIN
    EXEC(N'ALTER TABLE dbo.ai_industry_goal
        ADD committed_demand_quantity BIGINT NOT NULL
            CONSTRAINT DF_ai_industry_goal_committed_demand DEFAULT (0) WITH VALUES;');
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.ai_industry_goal')
      AND name = N'CK_ai_industry_goal_committed_demand'
)
BEGIN
    EXEC(N'ALTER TABLE dbo.ai_industry_goal
        ADD CONSTRAINT CK_ai_industry_goal_committed_demand
            CHECK (committed_demand_quantity >= 0 AND
                   committed_demand_quantity <= target_quantity);');
END;

COMMIT TRANSACTION;
GO
