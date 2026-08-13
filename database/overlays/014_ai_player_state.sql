SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.ai_player_state', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ai_player_state
    (
        character_id INT NOT NULL,
        plan_key VARCHAR(256) NOT NULL,
        role_index INT NOT NULL,
        active_role VARCHAR(32) NOT NULL,
        role_started_at DATETIME2(3) NOT NULL,
        observed_world_work BIT NOT NULL
            CONSTRAINT DF_ai_player_state_observed_world_work DEFAULT (0),
        completed_roles INT NOT NULL
            CONSTRAINT DF_ai_player_state_completed_roles DEFAULT (0),
        completed_cycles INT NOT NULL
            CONSTRAINT DF_ai_player_state_completed_cycles DEFAULT (0),
        last_transition_reason VARCHAR(128) NULL,
        state_revision INT NOT NULL
            CONSTRAINT DF_ai_player_state_revision DEFAULT (0),
        updated_at DATETIME2(3) NOT NULL
            CONSTRAINT DF_ai_player_state_updated_at DEFAULT SYSUTCDATETIME(),
        row_version ROWVERSION NOT NULL,
        CONSTRAINT PK_ai_player_state PRIMARY KEY CLUSTERED (character_id),
        CONSTRAINT CK_ai_player_state_character CHECK (character_id > 0),
        CONSTRAINT CK_ai_player_state_role_index CHECK (role_index >= 0),
        CONSTRAINT CK_ai_player_state_completed_roles CHECK (completed_roles >= 0),
        CONSTRAINT CK_ai_player_state_completed_cycles CHECK (completed_cycles >= 0),
        CONSTRAINT CK_ai_player_state_revision CHECK (state_revision >= 0)
    );
END;

COMMIT TRANSACTION;
GO
