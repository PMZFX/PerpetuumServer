SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.ai_combat_goal', N'assignment_key') IS NULL
BEGIN
    ALTER TABLE dbo.ai_combat_goal
        ADD assignment_key VARCHAR(128) NULL;
END;

COMMIT TRANSACTION;
GO
