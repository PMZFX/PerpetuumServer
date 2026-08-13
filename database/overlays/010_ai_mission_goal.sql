SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.ai_mission_goal', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ai_mission_goal
    (
        character_id INT NOT NULL,
        mission_category INT NOT NULL,
        mission_level INT NOT NULL,
        target_count INT NOT NULL,
        completed_count INT NOT NULL,
        phase VARCHAR(64) NOT NULL,
        mission_guid UNIQUEIDENTIFIER NULL,
        source_eid BIGINT NOT NULL,
        target_eid BIGINT NULL,
        blocked_reason VARCHAR(128) NULL,
        goal_revision INT NOT NULL
            CONSTRAINT DF_ai_mission_goal_revision DEFAULT (0),
        updated_at DATETIME2(3) NOT NULL
            CONSTRAINT DF_ai_mission_goal_updated_at DEFAULT SYSUTCDATETIME(),
        row_version ROWVERSION NOT NULL,
        CONSTRAINT PK_ai_mission_goal PRIMARY KEY CLUSTERED (character_id),
        CONSTRAINT CK_ai_mission_goal_character CHECK (character_id > 0),
        CONSTRAINT CK_ai_mission_goal_level CHECK (mission_level BETWEEN -1 AND 9),
        CONSTRAINT CK_ai_mission_goal_target_count CHECK (target_count > 0),
        CONSTRAINT CK_ai_mission_goal_completed_count CHECK
            (completed_count >= 0 AND completed_count <= target_count),
        CONSTRAINT CK_ai_mission_goal_source CHECK (source_eid > 0),
        CONSTRAINT CK_ai_mission_goal_target CHECK (target_eid IS NULL OR target_eid > 0),
        CONSTRAINT CK_ai_mission_goal_revision CHECK (goal_revision >= 0)
    );
END;

COMMIT TRANSACTION;
GO
