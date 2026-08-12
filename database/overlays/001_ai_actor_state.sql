SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.ai_actor_state', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ai_actor_state
    (
        character_id INT NOT NULL,
        behavior_name VARCHAR(64) NOT NULL,
        expected_robot_eid BIGINT NULL,
        observed_robot_eid BIGINT NULL,
        recovery_required BIT NOT NULL
            CONSTRAINT DF_ai_actor_state_recovery_required DEFAULT (0),
        recovery_reason VARCHAR(64) NULL,
        recovery_revision INT NOT NULL
            CONSTRAINT DF_ai_actor_state_recovery_revision DEFAULT (0),
        updated_at DATETIME2(3) NOT NULL
            CONSTRAINT DF_ai_actor_state_updated_at DEFAULT (SYSUTCDATETIME()),
        row_version ROWVERSION NOT NULL,
        CONSTRAINT PK_ai_actor_state PRIMARY KEY CLUSTERED (character_id),
        CONSTRAINT CK_ai_actor_state_character_id CHECK (character_id > 0),
        CONSTRAINT CK_ai_actor_state_recovery_revision CHECK (recovery_revision >= 0)
    );
END;

COMMIT TRANSACTION;
GO
