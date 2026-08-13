SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.ai_equipment_goal', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ai_equipment_goal
    (
        character_id INT NOT NULL,
        robot_definition INT NOT NULL,
        phase VARCHAR(64) NOT NULL,
        robot_eid BIGINT NULL,
        blocked_reason VARCHAR(128) NULL,
        goal_revision INT NOT NULL
            CONSTRAINT DF_ai_equipment_goal_revision DEFAULT (0),
        updated_at DATETIME2(3) NOT NULL
            CONSTRAINT DF_ai_equipment_goal_updated_at DEFAULT SYSUTCDATETIME(),
        row_version ROWVERSION NOT NULL,
        CONSTRAINT PK_ai_equipment_goal PRIMARY KEY CLUSTERED (character_id),
        CONSTRAINT CK_ai_equipment_goal_character CHECK (character_id > 0),
        CONSTRAINT CK_ai_equipment_goal_robot_definition CHECK (robot_definition > 0),
        CONSTRAINT CK_ai_equipment_goal_robot_eid CHECK (robot_eid IS NULL OR robot_eid > 0),
        CONSTRAINT CK_ai_equipment_goal_revision CHECK (goal_revision >= 0)
    );
END;

COMMIT TRANSACTION;
GO
