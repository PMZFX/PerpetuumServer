SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRANSACTION;

DECLARE @TransportMissionId INT =
(
    SELECT id
    FROM dbo.missions
    WHERE name = 'mission_tutorialchecklist_transport'
);

DECLARE @TrainingLocationId INT =
(
    SELECT id
    FROM dbo.missionlocations
    WHERE zoneid = 45 AND locationeid = 2432
);

IF @TransportMissionId IS NULL
    THROW 50000, 'Required Transport Training mission was not found.', 1;

IF @TrainingLocationId IS NULL
    THROW 50000, 'Required virtual training hub mission location was not found.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.npcflock f
    INNER JOIN dbo.npcpresence p ON p.id = f.presenceid
    WHERE p.spawnid = 48
      AND p.enabled = 1
      AND f.enabled = 1
      AND f.definition = 5334
      AND f.spawnoriginX BETWEEN 700 AND 725
      AND f.spawnoriginY BETWEEN 395 AND 420
)
    THROW 50000, 'Required persistent training Scarab flock was not found.', 1;

DECLARE @CombatMissionId INT =
(
    SELECT id
    FROM dbo.missions
    WHERE name = 'mission_syndicate_field_certification_combat'
);

IF @CombatMissionId IS NULL
BEGIN
    INSERT dbo.missions
    (
        name,
        title,
        description,
        missiontype,
        issuerid,
        isunique,
        missionpack,
        missionlevel,
        durationminutes,
        successmessage,
        failmessage,
        listable,
        alwaysenabled,
        rewardfee,
        locationid,
        behaviourtype,
        sourceagent
    )
    VALUES
    (
        'mission_syndicate_field_certification_combat',
        'mission_sfc_target_acquisition_title',
        'mission_sfc_target_acquisition_description',
        15,
        197,
        1,
        0,
        0,
        360,
        'mission_sfc_target_acquisition_success',
        NULL,
        1,
        0,
        15000,
        @TrainingLocationId,
        1,
        NULL
    );

    SET @CombatMissionId = CONVERT(INT, SCOPE_IDENTITY());
END;
ELSE
BEGIN
    UPDATE dbo.missions
    SET title = 'mission_sfc_target_acquisition_title',
        description = 'mission_sfc_target_acquisition_description',
        missiontype = 15,
        issuerid = 197,
        isunique = 1,
        missionlevel = 0,
        durationminutes = 360,
        successmessage = 'mission_sfc_target_acquisition_success',
        listable = 1,
        alwaysenabled = 0,
        rewardfee = 15000,
        locationid = @TrainingLocationId,
        behaviourtype = 1
    WHERE id = @CombatMissionId;
END;

IF EXISTS
(
    SELECT 1
    FROM dbo.missions
    WHERE id = @TransportMissionId
      AND missionidonsuccess IS NOT NULL
      AND missionidonsuccess <> @CombatMissionId
)
    THROW 50000, 'Transport Training already has a different successor mission.', 1;

UPDATE dbo.missions
SET missionidonsuccess = @CombatMissionId
WHERE id = @TransportMissionId;

DECLARE @ReachTargetId INT =
(
    SELECT id FROM dbo.missiontargets
    WHERE missionid = @CombatMissionId
      AND name = 'mission_sfc_target_acquisition_reach'
);

IF @ReachTargetId IS NULL
BEGIN
    INSERT dbo.missiontargets
    (
        name, description, missionid, targettype, quantity,
        targetpositionx, targetpositiony, targetpositionrange, targetpositionzone,
        checkposition, targetorder, displayorder, optional, hidden,
        spawnnpcs, snaptonextstructure, usequantityonly
    )
    VALUES
    (
        'mission_sfc_target_acquisition_reach',
        'mission_sfc_target_acquisition_reach',
        @CombatMissionId, 3, NULL,
        680, 420, 35, 45,
        1, 0, 0, 0, 0,
        0, 0, 0
    );
END;
ELSE
BEGIN
    UPDATE dbo.missiontargets
    SET description = 'mission_sfc_target_acquisition_reach',
        targettype = 3,
        definition = NULL,
        quantity = NULL,
        targetpositionx = 680,
        targetpositiony = 420,
        targetpositionrange = 35,
        targetpositionzone = 45,
        checkposition = 1,
        targetorder = 0,
        displayorder = 0,
        optional = 0,
        hidden = 0,
        usequantityonly = 0
    WHERE id = @ReachTargetId;
END;

DECLARE @LockTargetId INT =
(
    SELECT id FROM dbo.missiontargets
    WHERE missionid = @CombatMissionId
      AND name = 'mission_sfc_target_acquisition_lock'
);

IF @LockTargetId IS NULL
BEGIN
    INSERT dbo.missiontargets
    (
        name, description, missionid, targettype, definition, quantity,
        targetpositionx, targetpositiony, targetpositionrange, targetpositionzone,
        checkposition, targetorder, displayorder, optional, hidden,
        spawnnpcs, snaptonextstructure, usequantityonly
    )
    VALUES
    (
        'mission_sfc_target_acquisition_lock',
        'mission_sfc_target_acquisition_lock',
        @CombatMissionId, 23, 5334, 1,
        713, 406, 80, 45,
        1, 1, 1, 0, 0,
        0, 0, 0
    );
END;
ELSE
BEGIN
    UPDATE dbo.missiontargets
    SET description = 'mission_sfc_target_acquisition_lock',
        targettype = 23,
        definition = 5334,
        quantity = 1,
        targetpositionx = 713,
        targetpositiony = 406,
        targetpositionrange = 80,
        targetpositionzone = 45,
        checkposition = 1,
        targetorder = 1,
        displayorder = 1,
        optional = 0,
        hidden = 0,
        usequantityonly = 0
    WHERE id = @LockTargetId;
END;

DECLARE @DestroyTargetId INT =
(
    SELECT id FROM dbo.missiontargets
    WHERE missionid = @CombatMissionId
      AND name = 'mission_sfc_target_acquisition_destroy'
);

IF @DestroyTargetId IS NULL
BEGIN
    INSERT dbo.missiontargets
    (
        name, description, missionid, targettype, definition, quantity,
        targetpositionx, targetpositiony, targetpositionrange, targetpositionzone,
        checkposition, targetorder, displayorder, optional, hidden,
        spawnnpcs, snaptonextstructure, usequantityonly
    )
    VALUES
    (
        'mission_sfc_target_acquisition_destroy',
        'mission_sfc_target_acquisition_destroy',
        @CombatMissionId, 4, 5334, 1,
        713, 406, 80, 45,
        1, 2, 2, 0, 0,
        0, 0, 0
    );
END;
ELSE
BEGIN
    UPDATE dbo.missiontargets
    SET description = 'mission_sfc_target_acquisition_destroy',
        targettype = 4,
        definition = 5334,
        quantity = 1,
        targetpositionx = 713,
        targetpositiony = 406,
        targetpositionrange = 80,
        targetpositionzone = 45,
        checkposition = 1,
        targetorder = 2,
        displayorder = 2,
        optional = 0,
        hidden = 0,
        usequantityonly = 0
    WHERE id = @DestroyTargetId;
END;

IF EXISTS
(
    SELECT 1
    FROM dbo.missiontargets
    WHERE missionid = @CombatMissionId
      AND name NOT IN
      (
          'mission_sfc_target_acquisition_reach',
          'mission_sfc_target_acquisition_lock',
          'mission_sfc_target_acquisition_destroy'
      )
)
    THROW 50000, 'Combat certification contains an unexpected objective.', 1;

COMMIT TRANSACTION;
GO
