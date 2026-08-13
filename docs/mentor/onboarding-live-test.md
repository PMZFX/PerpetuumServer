# Onboarding Prototype Live Test

The entry test is cleanest with a newly created training character. A character already enrolled
in Transport training can also relog to test the cargo-capacity recovery.

## Entry and persistence

1. Create a new account/character and enter the training hub.
2. The closed client may open **Rookie Checklist** automatically. Confirm every legacy task is
   already checked, close the cosmetic panel, and do not interact with it further.
3. Confirm the ordinary interface—including Assignments, cargo, fitting, and Deploy—is available
   immediately without manually completing any stock checklist tasks.
4. Confirm **Transport training** is already active without manually accepting it or forcing a
   modal assignment window.
5. Confirm Mentor posts a short private first-shift briefing identifying Transport training.
6. Confirm no legacy "begin tutorial" mail or tutorial-arrival Help announcement appears.
7. Close and reopen the assignment window, then relog once if practical; confirm the assignment is
   still active at the same objective.
8. In Mentor, ask `What is my current objective?` and confirm the answer reflects the active
   assignment rather than giving only generic tutorial advice.

## Playable sequence

1. Confirm the starter Arkhe is already active, then deploy.
2. Move to objective A and confirm it completes.
3. Confirm Mentor immediately explains how to approach and activate Item Supply. Activate the item
   supply at objective B, remain still while it operates, and confirm the SynSec
   container enters cargo without a cargo-full error. The onboarding Arkhe should report at least
   52 cargo capacity; the override applies only to that robot inventory.
4. Confirm Mentor immediately explains objective C and item submission. Move to objective C,
   interact with the delivery object, and submit the container.
5. Confirm the assignment completes, awards 10,000 NIC exactly once, and Mentor acknowledges the
   completed shift without telling the player that the starter Arkhe will be reclaimed.

## Target Acquisition sequence

1. Confirm **Target Acquisition** appears automatically after Transport Training completes; no
   manual acceptance or relog should be necessary.
2. Follow its range marker and confirm Mentor tells you to use `R` for the default primary lock.
3. Select a **training Scarab**, press `R`, and wait for the lock to complete. Locking a dummy decoy
   or a Scarab outside the marked range must not advance the objective.
4. Confirm Mentor acknowledges the lock and tells you to activate the fitted autocannon.
5. Destroy that Scarab and confirm the assignment completes, awards 15,000 NIC exactly once, and
   Mentor summarizes the select → lock → module combat loop.
6. Relog once while Target Acquisition is active if practical. Its objective should persist and
   the existing range NPCs should remain available; the assignment must not depend on a temporary
   spawned target surviving the server session.

Record anything that is unclear before consulting the full assignment description. In particular,
note whether the assignment becomes visible automatically, whether objective markers are obvious,
and whether Mentor states the current objective accurately after each transition.

Do not graduate this test character yet. The client-facing training compatibility flag must be
validated against the later server-owned graduation slice before the legacy training-exit flow is
retired.

## Server evidence

Successful enrollment emits:

```text
event=field_certification_enrolled mission_id=... mission_guid=... zone_id=45 reward_fee=10000
```

Successful post-selection presentation emits:

```text
event=field_certification_presented mission_id=... mission_guid=... attempt=...
```

Successful cargo preparation emits once per starter inventory:

```text
event=starter_cargo_reserved robot_eid=... capacity=52 load=... mission_volume=50
```

Each deterministic post-objective handoff emits:

```text
event=field_certification_guidance mission_id=... mission_guid=... target_type=...
```

A content mismatch emits `event=field_certification_skipped` and a deterministic reason. It must
not prevent character creation.
