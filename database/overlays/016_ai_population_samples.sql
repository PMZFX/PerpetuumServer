SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.ai_population_sample', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ai_population_sample
    (
        sample_id BIGINT IDENTITY(1,1) NOT NULL,
        observed_at DATETIME2(3) NOT NULL,
        configured_actors INT NOT NULL,
        registered_actors INT NOT NULL,
        active_actors INT NOT NULL,
        standby_actors INT NOT NULL,
        suspended_actors INT NOT NULL,
        faulted_actors INT NOT NULL,
        updates_completed INT NOT NULL,
        update_failures INT NOT NULL,
        registration_failures INT NOT NULL,
        maximum_update_ms FLOAT NOT NULL,
        maximum_actor_elapsed_ms FLOAT NOT NULL,
        scheduler_backlog INT NOT NULL,
        behavior_counts VARCHAR(1024) NOT NULL,
        CONSTRAINT PK_ai_population_sample PRIMARY KEY CLUSTERED (sample_id),
        CONSTRAINT CK_ai_population_sample_configured CHECK (configured_actors >= 0),
        CONSTRAINT CK_ai_population_sample_registered CHECK
            (registered_actors >= 0 AND registered_actors <= configured_actors),
        CONSTRAINT CK_ai_population_sample_statuses CHECK
            (active_actors >= 0 AND standby_actors >= 0 AND
             suspended_actors >= 0 AND faulted_actors >= 0 AND
             active_actors + standby_actors + suspended_actors + faulted_actors <= registered_actors),
        CONSTRAINT CK_ai_population_sample_updates CHECK
            (updates_completed >= 0 AND update_failures >= 0 AND
             update_failures <= updates_completed),
        CONSTRAINT CK_ai_population_sample_registration_failures CHECK
            (registration_failures >= 0),
        CONSTRAINT CK_ai_population_sample_timings CHECK
            (maximum_update_ms >= 0 AND maximum_actor_elapsed_ms >= 0),
        CONSTRAINT CK_ai_population_sample_backlog CHECK (scheduler_backlog >= 0)
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.ai_population_sample')
      AND name = N'IX_ai_population_sample_observed_at'
)
BEGIN
    CREATE INDEX IX_ai_population_sample_observed_at
        ON dbo.ai_population_sample (observed_at DESC);
END;

COMMIT TRANSACTION;
GO
