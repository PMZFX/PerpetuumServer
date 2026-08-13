SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.ai_industry_goal', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ai_industry_goal
    (
        character_id INT NOT NULL,
        target_definition INT NOT NULL,
        target_quantity BIGINT NOT NULL,
        initial_inventory_quantity BIGINT NOT NULL,
        mill_facility_eid BIGINT NOT NULL,
        phase VARCHAR(64) NOT NULL,
        line_id INT NULL,
        production_id INT NULL,
        procurement_json NVARCHAR(MAX) NULL,
        blocked_reason VARCHAR(128) NULL,
        updated_at DATETIME2(3) NOT NULL
            CONSTRAINT DF_ai_industry_goal_updated_at DEFAULT SYSUTCDATETIME(),
        row_version ROWVERSION NOT NULL,
        CONSTRAINT PK_ai_industry_goal PRIMARY KEY CLUSTERED (character_id),
        CONSTRAINT CK_ai_industry_goal_character CHECK (character_id > 0),
        CONSTRAINT CK_ai_industry_goal_definition CHECK (target_definition > 0),
        CONSTRAINT CK_ai_industry_goal_quantity CHECK
            (target_quantity > 0 AND initial_inventory_quantity >= 0),
        CONSTRAINT CK_ai_industry_goal_facility CHECK (mill_facility_eid > 0),
        CONSTRAINT CK_ai_industry_goal_procurement_json CHECK
            (procurement_json IS NULL OR ISJSON(procurement_json) = 1)
    );
END;

COMMIT TRANSACTION;
GO
