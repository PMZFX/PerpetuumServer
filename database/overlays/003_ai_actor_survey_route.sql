SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.ai_actor_work_state', N'U') IS NULL
    THROW 50000, 'Overlay 002_ai_actor_work_state.sql must be applied first.', 1;

IF COL_LENGTH(N'dbo.ai_actor_work_state', N'survey_route') IS NULL
BEGIN
    ALTER TABLE dbo.ai_actor_work_state
        ADD survey_route NVARCHAR(MAX) NULL;
END;

COMMIT TRANSACTION;
GO
