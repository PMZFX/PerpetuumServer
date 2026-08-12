# Native Linux P31 server

## Scope

This release line ports the five headless P31 projects to SDK-style .NET 10:

1. `Perpetuum.ExportedTypes`
2. `Perpetuum`
3. `Perpetuum.RequestHandlers`
4. `Perpetuum.Bootstrapper`
5. `Perpetuum.Server`

It preserves the P31 client protocol and gameplay behavior. The WPF AdminTool
and Windows Service host remain Windows-only and are not part of the Linux
dependency graph.

## External prerequisites

The repository does not redistribute Perpetuum's client or dedicated-server
runtime assets. A working deployment needs:

- a matching P31 `perpetuumsa` SQL Server database initialized from OPDB;
- the legitimately acquired dedicated-server runtime data, including
  `layers`, `customDictionary`, and `plantrules`; and
- a local `perpetuum.ini` containing the listener and database settings.

The game server writes persistent terrain layers during operation and clean
shutdown, so its runtime-data mount must be writable by the container user.
Keep credentials in a local ignored file or secret store; never commit
`perpetuum.ini` with a live password.

## Build

From the repository root:

```bash
docker build --tag perpetuum-server:linux-p31 .
docker run --rm perpetuum-server:linux-p31 --help
```

The build uses the SDK and runtime image digests recorded in `Dockerfile` and
does not require the external game data.

## Configuration

The console host reads `/game/perpetuum.ini` by default. A minimal shape is:

```json
{
  "ListenerPort": 17700,
  "EnableUpnp": false,
  "ConnectionString": "Server=database,1433;Database=perpetuumsa;User ID=sa;Password=CHANGE_ME;Encrypt=True;TrustServerCertificate=True",
  "PersonalConfig": "startup_standalone"
}
```

Automatic UPnP is deliberately unavailable. Configure explicit firewall and
port-forwarding rules instead. Do not publish SQL Server's port to an
untrusted network.

## Run

On a user-defined Docker network containing a SQL Server service named
`database`:

```bash
docker run --detach \
  --name perpetuum-server \
  --network perpetuum \
  --user "$(id -u):$(id -g)" \
  --publish 17700-17759:17700-17759 \
  --volume /absolute/path/to/p31-runtime:/game:Z \
  perpetuum-server:linux-p31
```

P31 assigns the relay to `17700` and one sequential listener to each of its 59
enabled zones. A database with a different enabled-zone count may require a
different upper port bound. Linux host networking is also suitable when SQL
Server and the game host are intentionally managed on the same machine.

Follow startup and verify the terminal state:

```bash
docker logs --follow perpetuum-server
```

A successful P31 boot initializes the database connection, loads zone 140,
runs consistency checks, and reports:

```text
>>>> Perpetuum Server State : [Online]
```

Autonomous character hosting is opt-in. Existing configuration files remain
disabled by default. The initial idle lifecycle can be configured with normal
character IDs:

```json
"Autonomous": {
  "Enabled": false,
  "TickIntervalMilliseconds": 500,
  "MaxConsecutiveFailures": 3,
  "Actors": [
    {
      "CharacterId": 123,
      "Enabled": true,
      "Behavior": "idle"
    }
  ]
}
```

Only dedicated characters should be enabled. A human relay session selecting a
configured character suspends its autonomous controller; it resumes only after
the human relay and zone sessions release that character. The idle behavior
performs no actions and is intended to validate lifecycle and configuration.

The first visible field behavior is a bounded patrol. It uses the same typed
undock, movement-input, and dock services as a client character. The AI never
sets its position or speed: A* selects ordinary walkable cells, while the
existing player simulation continues to enforce robot speed, slope, collision,
effects, and docking range. Path searches are locally bounded and a stationary
actor stops, replans twice, then reports a blocked route.

```json
"Autonomous": {
  "Enabled": true,
  "TickIntervalMilliseconds": 500,
  "MaxConsecutiveFailures": 3,
  "Actors": [
    {
      "CharacterId": 123,
      "Enabled": true,
      "Behavior": "patrol",
      "RecoveryRevision": 0,
      "Patrol": {
        "Radius": 14,
        "Throttle": 0.45,
        "DockedDwellSeconds": 15,
        "FieldDwellSeconds": 2,
        "Threat": {
          "Enabled": true,
          "ResponseRange": 35.0,
          "DockedDwellSeconds": 60
        },
        "Defense": {
          "Enabled": false,
          "ResponseRange": 60.0,
          "LockTimeoutSeconds": 8,
          "MaxEngagementSeconds": 20
        }
      }
    }
  ]
}
```

On each field entry, the headless session applies the same teleport-sickness
and invulnerability effects as a normal zone session. A human login still wins
ownership immediately; the patrol plan is discarded and is rebuilt only after
the human session releases the character.

If a controlled shutdown persists an autonomous character in the field, the
next enabled startup reloads that real character through the same player loader
used by client zone authentication. Patrol recovery prioritizes a normal return
to the character's current docking base before beginning another cycle.
Configured dock dwell is a minimum: the actor also waits for the character's
authoritative next-undock time, so a short dwell cannot bypass or repeatedly
fail against the normal post-dock cooldown.

Layer persistence writes and verifies a same-directory temporary file, then
overwrites the prior layer with one filesystem move. It does not delete the
prior layer first, avoiding a missing-layer window if container shutdown is
interrupted between those operations.

Patrol perception projects the real player's existing visible-unit set; it
does not enumerate the zone. This is the same set that drives client unit
enter/exit packets, so detection, stealth, gang visibility, and GM-stealth
rules have already been applied. A unit is considered hostile using its normal
server relationship toward the player. Hostiles beyond `ResponseRange` remain
observable but do not trigger a response.

The initial threat policy is deliberately defensive. A visible hostile inside
the configured range causes a deploying actor to dock, or an outbound/dwelling
actor to return along its normal route and dock. An actor already returning or
docking continues that work instead of restarting it. After a threat-driven
dock it waits `Threat.DockedDwellSeconds` before another patrol. The policy
does not target, activate modules, modify combat state, or grant hidden world
knowledge.

Targeting and module operations are nevertheless exposed as shared,
authenticated game actions for later behaviors. A unit can be submitted for
locking only when it is in the real player's maintained visible set. The normal
asynchronous lock handler still decides lock slots, range, lockability, target
state, and completion time; terrain-lock height is read from server terrain.
Module activation and ammunition operations continue through the existing
module state machine and inventory transactions, preserving lock, range, line
of sight, core, ammunition, aggression, PvP, cycle, and damage rules.

Patrol requires the project database overlay that creates
`dbo.ai_actor_state`. It durably records the expected robot. If that robot is
destroyed, removed, or replaced, navigation stops and a recovery audit event is
emitted; restarting the server or receiving a starter robot does not clear the
condition. Normal player-death processing remains responsible for docking,
loot, insurance, robot disposal, and replacement selection. After a legitimate
replacement has been selected, increment that actor's `RecoveryRevision` and
restart or reload the actor. A revision is consumed only when an active robot
exists, and the acknowledgment is audited. A future replacement planner will
use the same state-store boundary instead of bypassing it.

Defensive combat is separately opt-in through `Patrol.Defense.Enabled`. It
subscribes to the controlled player's normal positive-damage event and
considers only that actual source; merely seeing a hostile never authorizes a
shot. The source must
still be alive, inside `ResponseRange`, and present in the player's maintained
visible set. The behavior requests one normal primary lock, waits no longer
than `LockTimeoutSeconds`, and activates only fitted weapons with loaded
ammunition. Normal module rules still decide range, line of sight, core,
aggression, PvP legality, cycles, and damage.

The response has a hard `MaxEngagementSeconds` limit measured from the first
damage event. More damage cannot extend it, and a different attacker cannot
cause target thrashing during that response. The patrol retreats at the same
time, deactivates weapons and removes its own defense lock when the source is
lost or the limit expires, then waits for normal aggression and docking rules.
This feature does not choose targets proactively, create ammunition, repair the
robot, or replace a destroyed robot.

The first economic field behavior is opt-in as `mining`. It currently covers
deploy, scan, travel, lock, drill, return, and dock. The active robot must
already have a tile geoscanner and mining turret fitted with loaded ammunition
for the configured material.

```json
{
  "CharacterId": 123,
  "Enabled": true,
  "Behavior": "mining",
  "RecoveryRevision": 0,
  "Mining": {
    "Material": "Titan",
    "Throttle": 0.45,
    "CargoFillRatio": 0.75,
    "DockedDwellSeconds": 15,
    "ScanTimeoutSeconds": 30,
    "LockTimeoutSeconds": 8,
    "MaxMiningSeconds": 300,
    "MaxScanAttempts": 3,
    "SurveyStepDistance": 11,
    "MaxSurveySites": 48,
    "Resupply": {
      "Enabled": true,
      "ReloadBelowRatio": 0.5,
      "RetrySeconds": 60
    },
    "Market": {
      "Enabled": true,
      "SellAllRawMaterials": true,
      "MinimumUnitPrice": 1.0,
      "ListPriceFactor": 0.98,
      "OrderDurationHours": 24,
      "RetrySeconds": 60
    },
    "Threat": {
      "Enabled": true,
      "ResponseRange": 35.0,
      "DockedDwellSeconds": 60
    }
  }
}
```

The behavior cannot inspect mineral layers. Its target comes from the exact
noisy tile grid produced by a fitted scanner and sent to the game client.
Equipment selection sees only the robot fitting and loaded ammunition; cargo
decisions see only the active robot's own inventory and capacity. Scanner
probes and mining charges are consumed through the normal module state machine.
An empty scan advances through at most `MaxSurveySites` deterministic search
positions in a contiguous square spiral around the terminal spawn, separated by
`SurveyStepDistance`. Every position must be reached by normal pathfinding and
movement before another probe is consumed. Survey positions are not mineral
facts and are not persisted as deposit targets; only a non-empty scanner
observation can authorize travel to a mining tile. The default 48-site bound
covers three rings and resets after each completed dock/undock cycle. The
configured site count and spacing are also constrained to keep every survey
position within the navigator's safe return range.

Mining progress requires `dbo.ai_actor_work_state`, created by
`database/overlays/002_ai_actor_work_state.sql`. Each transition records the
base, zone, return origin, observed target, and material. A restart resumes a
target only when those facts still match; otherwise the actor discards stale
coordinates and uses normal base recovery. The separate `dbo.ai_actor_state`
robot identity guard remains authoritative.

Market participation remains separately opt-in. When `Market.Enabled` is false,
a docked miner at or above `CargoFillRatio` stays docked and emits
`mining_cargo_ready`. When enabled, the miner sells one eligible raw-material
stack per autonomous tick directly from its active robot cargo through the
normal market sell-order action. `SellAllRawMaterials` includes mining
byproducts; disabling it restricts sales to the configured primary material.

Pricing uses information visible in the local market UI. An eligible best buy
order is accepted first. Otherwise the bot lists at the local trade average
times `ListPriceFactor`, protected by `MinimumUnitPrice`; without useful market
history it lists at that minimum. Normal order slots, listing fees, wallet
balance, item ownership, saleability, and market availability all apply. A
rejected sale is audited as `mining_market_blocked` and retried after
`RetrySeconds`; ore is never discarded and credits are never granted directly.

Docked resupply is separately controlled by `Resupply`. Before undocking, the
miner examines only the fitted modules and ammunition already in its active
robot cargo. It reloads one tile probe or matching mining-charge stack at a
time through the same docked `equipAmmo` action used by the client. A module is
eligible below `ReloadBelowRatio`; a different or empty load is also eligible.
If no fitted tile scanner and drill have usable charges after resupply, the
miner stays docked, emits `mining_equipment_ready_required`, and checks again
after `RetrySeconds`. It does not create, buy, or silently refill ammunition.

Stop with enough time for zone-layer persistence:

```bash
docker stop --timeout 20 perpetuum-server
```

The process should report `Stopping`, then `Off`, and exit with status 0.

## Stock client

In the stock Steam client, add the Linux host's reachable address as a private
server. The private-server flow uses normal username/password authentication;
Steam supplies and launches the client but is not the private-server login
mechanism.

Normal registration is controlled by `dbo.serverinfo.isopen`. Keep
`isbroadcast` disabled for a private development instance. The P31
`accountOpenCreate` handler creates normal accounts and enforces one creation
per relay session.

## Validated baseline

The `linux-p31` baseline has been exercised against an isolated exact-P31
database and runtime:

- all 40 P31 database patch calls applied;
- 291 tables and 6,232 entity defaults passed the recorded invariants;
- all 59 enabled zones loaded and reached `Online`;
- relay and zone listeners accepted stock-client connections;
- login, character creation, market purchase, undock, movement, dock, and
  clean disconnect succeeded; and
- accounts and characters persisted across a controlled server restart.

Remaining `System.Drawing.Common` analyzer warnings belong to optional
debug/admin bitmap handlers, not normal server startup. The legacy Rijndael
zone-ticket implementation remains in place to preserve client wire
compatibility until a dedicated protocol test covers its replacement.
