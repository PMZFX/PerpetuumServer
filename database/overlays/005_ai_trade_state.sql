SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.ai_market_memory', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.CK_ai_market_memory_location', N'C') IS NOT NULL
BEGIN
    ALTER TABLE dbo.ai_market_memory DROP CONSTRAINT CK_ai_market_memory_location;
    ALTER TABLE dbo.ai_market_memory WITH CHECK ADD CONSTRAINT CK_ai_market_memory_location
        CHECK (market_eid > 0 AND docking_base_eid > 0 AND zone_id >= 0);
END;

IF OBJECT_ID(N'dbo.ai_trade_state', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ai_trade_state
    (
        character_id INT NOT NULL,
        item_definition INT NOT NULL,
        quantity_remaining INT NOT NULL,
        unit_cost FLOAT NOT NULL,
        source_market_eid BIGINT NOT NULL,
        source_base_eid BIGINT NOT NULL,
        source_zone_id INT NOT NULL,
        destination_market_eid BIGINT NOT NULL,
        destination_base_eid BIGINT NOT NULL,
        destination_zone_id INT NOT NULL,
        acquired_at DATETIME2(3) NOT NULL,
        updated_at DATETIME2(3) NOT NULL
            CONSTRAINT DF_ai_trade_state_updated_at DEFAULT SYSUTCDATETIME(),
        row_version ROWVERSION NOT NULL,
        CONSTRAINT PK_ai_trade_state PRIMARY KEY CLUSTERED (character_id),
        CONSTRAINT CK_ai_trade_state_character CHECK (character_id > 0),
        CONSTRAINT CK_ai_trade_state_definition CHECK (item_definition > 0),
        CONSTRAINT CK_ai_trade_state_quantity CHECK (quantity_remaining > 0),
        CONSTRAINT CK_ai_trade_state_unit_cost CHECK (unit_cost > 0),
        CONSTRAINT CK_ai_trade_state_source CHECK
            (source_market_eid > 0 AND source_base_eid > 0 AND source_zone_id >= 0),
        CONSTRAINT CK_ai_trade_state_destination CHECK
            (destination_market_eid > 0 AND destination_base_eid > 0 AND destination_zone_id >= 0),
        CONSTRAINT CK_ai_trade_state_distinct_markets CHECK
            (source_market_eid <> destination_market_eid)
    );
END;

IF OBJECT_ID(N'dbo.ai_trade_state', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.CK_ai_trade_state_source', N'C') IS NOT NULL
BEGIN
    ALTER TABLE dbo.ai_trade_state DROP CONSTRAINT CK_ai_trade_state_source;
    ALTER TABLE dbo.ai_trade_state WITH CHECK ADD CONSTRAINT CK_ai_trade_state_source
        CHECK (source_market_eid > 0 AND source_base_eid > 0 AND source_zone_id >= 0);
END;

IF OBJECT_ID(N'dbo.ai_trade_state', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.CK_ai_trade_state_destination', N'C') IS NOT NULL
BEGIN
    ALTER TABLE dbo.ai_trade_state DROP CONSTRAINT CK_ai_trade_state_destination;
    ALTER TABLE dbo.ai_trade_state WITH CHECK ADD CONSTRAINT CK_ai_trade_state_destination
        CHECK (destination_market_eid > 0 AND destination_base_eid > 0 AND destination_zone_id >= 0);
END;

COMMIT TRANSACTION;
GO
