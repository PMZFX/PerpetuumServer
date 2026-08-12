SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.ai_actor_work_state', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ai_actor_work_state
    (
        character_id INT NOT NULL,
        behavior_name VARCHAR(64) NOT NULL,
        phase VARCHAR(64) NOT NULL,
        docking_base_eid BIGINT NULL,
        zone_id INT NULL,
        origin_x FLOAT NULL,
        origin_y FLOAT NULL,
        target_x FLOAT NULL,
        target_y FLOAT NULL,
        material_type INT NULL,
        updated_at DATETIME2(3) NOT NULL
            CONSTRAINT DF_ai_actor_work_state_updated_at DEFAULT (SYSUTCDATETIME()),
        row_version ROWVERSION NOT NULL,
        CONSTRAINT PK_ai_actor_work_state PRIMARY KEY CLUSTERED (character_id),
        CONSTRAINT CK_ai_actor_work_state_character_id CHECK (character_id > 0)
    );
END;

COMMIT TRANSACTION;
GO
