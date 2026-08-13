SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.ai_supply_request', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ai_supply_request
    (
        request_id BIGINT IDENTITY(1,1) NOT NULL,
        requester_character_id INT NOT NULL,
        goal_definition INT NOT NULL,
        item_definition INT NOT NULL,
        quantity_required BIGINT NOT NULL,
        destination_base_eid BIGINT NOT NULL,
        active BIT NOT NULL
            CONSTRAINT DF_ai_supply_request_active DEFAULT (1),
        expires_at DATETIME2(3) NOT NULL,
        created_at DATETIME2(3) NOT NULL
            CONSTRAINT DF_ai_supply_request_created_at DEFAULT SYSUTCDATETIME(),
        updated_at DATETIME2(3) NOT NULL
            CONSTRAINT DF_ai_supply_request_updated_at DEFAULT SYSUTCDATETIME(),
        row_version ROWVERSION NOT NULL,
        CONSTRAINT PK_ai_supply_request PRIMARY KEY CLUSTERED (request_id),
        CONSTRAINT UQ_ai_supply_request_goal_item UNIQUE
            (requester_character_id, goal_definition, item_definition),
        CONSTRAINT CK_ai_supply_request_requester CHECK (requester_character_id > 0),
        CONSTRAINT CK_ai_supply_request_goal CHECK (goal_definition > 0),
        CONSTRAINT CK_ai_supply_request_item CHECK (item_definition > 0),
        CONSTRAINT CK_ai_supply_request_quantity CHECK (quantity_required > 0),
        CONSTRAINT CK_ai_supply_request_destination CHECK (destination_base_eid > 0)
    );

    CREATE INDEX IX_ai_supply_request_open
        ON dbo.ai_supply_request
            (active, item_definition, destination_base_eid, expires_at)
        INCLUDE (quantity_required, requester_character_id, goal_definition, created_at);
END;

IF OBJECT_ID(N'dbo.ai_supply_reservation', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ai_supply_reservation
    (
        reservation_id BIGINT IDENTITY(1,1) NOT NULL,
        request_id BIGINT NOT NULL,
        supplier_character_id INT NOT NULL,
        quantity INT NOT NULL,
        phase VARCHAR(32) NOT NULL,
        active BIT NOT NULL
            CONSTRAINT DF_ai_supply_reservation_active DEFAULT (1),
        release_reason VARCHAR(128) NULL,
        expires_at DATETIME2(3) NOT NULL,
        created_at DATETIME2(3) NOT NULL
            CONSTRAINT DF_ai_supply_reservation_created_at DEFAULT SYSUTCDATETIME(),
        updated_at DATETIME2(3) NOT NULL
            CONSTRAINT DF_ai_supply_reservation_updated_at DEFAULT SYSUTCDATETIME(),
        row_version ROWVERSION NOT NULL,
        CONSTRAINT PK_ai_supply_reservation PRIMARY KEY CLUSTERED (reservation_id),
        CONSTRAINT FK_ai_supply_reservation_request FOREIGN KEY (request_id)
            REFERENCES dbo.ai_supply_request(request_id),
        CONSTRAINT CK_ai_supply_reservation_supplier CHECK (supplier_character_id > 0),
        CONSTRAINT CK_ai_supply_reservation_quantity CHECK (quantity > 0),
        CONSTRAINT CK_ai_supply_reservation_phase CHECK
            (phase IN ('Reserved', 'Listed', 'Released'))
    );

    CREATE UNIQUE INDEX UX_ai_supply_reservation_active_supplier
        ON dbo.ai_supply_reservation(supplier_character_id)
        WHERE active = 1;

    CREATE INDEX IX_ai_supply_reservation_request
        ON dbo.ai_supply_reservation(request_id, active, expires_at)
        INCLUDE (supplier_character_id, quantity, phase);
END;

COMMIT TRANSACTION;
GO
