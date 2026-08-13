SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.ai_combat_goal', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ai_combat_goal
    (
        character_id INT NOT NULL,
        target_count INT NOT NULL,
        completed_count INT NOT NULL,
        max_losses INT NOT NULL,
        loss_count INT NOT NULL,
        phase VARCHAR(64) NOT NULL,
        target_eid BIGINT NULL,
        lock_id BIGINT NULL,
        last_lost_robot_eid BIGINT NULL,
        blocked_reason VARCHAR(128) NULL,
        phase_started_at DATETIME2(3) NOT NULL,
        engagement_started_at DATETIME2(3) NULL,
        goal_revision INT NOT NULL
            CONSTRAINT DF_ai_combat_goal_revision DEFAULT (0),
        updated_at DATETIME2(3) NOT NULL
            CONSTRAINT DF_ai_combat_goal_updated_at DEFAULT SYSUTCDATETIME(),
        row_version ROWVERSION NOT NULL,
        CONSTRAINT PK_ai_combat_goal PRIMARY KEY CLUSTERED (character_id),
        CONSTRAINT CK_ai_combat_goal_character CHECK (character_id > 0),
        CONSTRAINT CK_ai_combat_goal_target_count CHECK (target_count > 0),
        CONSTRAINT CK_ai_combat_goal_completed_count CHECK
            (completed_count >= 0 AND completed_count <= target_count),
        CONSTRAINT CK_ai_combat_goal_max_losses CHECK (max_losses > 0),
        CONSTRAINT CK_ai_combat_goal_loss_count CHECK (loss_count >= 0),
        CONSTRAINT CK_ai_combat_goal_target CHECK (target_eid IS NULL OR target_eid > 0),
        CONSTRAINT CK_ai_combat_goal_lock CHECK (lock_id IS NULL OR lock_id > 0),
        CONSTRAINT CK_ai_combat_goal_lost_robot CHECK
            (last_lost_robot_eid IS NULL OR last_lost_robot_eid > 0),
        CONSTRAINT CK_ai_combat_goal_revision CHECK (goal_revision >= 0)
    );
END;

COMMIT TRANSACTION;
GO
