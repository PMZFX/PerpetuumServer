SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.ai_market_memory', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ai_market_memory
    (
        character_id INT NOT NULL,
        market_eid BIGINT NOT NULL,
        docking_base_eid BIGINT NOT NULL,
        zone_id INT NOT NULL,
        item_definition INT NOT NULL,
        best_buy_price FLOAT NULL,
        best_buy_quantity INT NULL,
        best_sell_price FLOAT NULL,
        best_sell_quantity INT NULL,
        average_price FLOAT NULL,
        observed_at DATETIME2(3) NOT NULL,
        row_version ROWVERSION NOT NULL,
        CONSTRAINT PK_ai_market_memory PRIMARY KEY CLUSTERED
            (character_id, market_eid, item_definition),
        CONSTRAINT CK_ai_market_memory_character CHECK (character_id > 0),
        CONSTRAINT CK_ai_market_memory_location CHECK
            (market_eid > 0 AND docking_base_eid > 0 AND zone_id > 0),
        CONSTRAINT CK_ai_market_memory_definition CHECK (item_definition > 0),
        CONSTRAINT CK_ai_market_memory_buy_price CHECK
            (best_buy_price IS NULL OR best_buy_price > 0),
        CONSTRAINT CK_ai_market_memory_sell_price CHECK
            (best_sell_price IS NULL OR best_sell_price > 0),
        CONSTRAINT CK_ai_market_memory_average_price CHECK
            (average_price IS NULL OR average_price > 0),
        CONSTRAINT CK_ai_market_memory_buy_quantity CHECK
            (best_buy_quantity IS NULL OR best_buy_quantity > 0),
        CONSTRAINT CK_ai_market_memory_sell_quantity CHECK
            (best_sell_quantity IS NULL OR best_sell_quantity > 0)
    );
END;

COMMIT TRANSACTION;
GO
