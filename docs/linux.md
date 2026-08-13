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

Concurrent development servers must not share a writable runtime-data tree or
game database. Give each stack its own Docker network, SQL volume/database, and
runtime-data copy. Set the second stack's `ListenerPort` to a non-overlapping
range such as `17800`, then publish `17800-17859:17800-17859`. Do not merely
remap host port `17800` to an internal relay on `17700`: zone handoff responses
advertise the sequential ports selected from `ListenerPort`, so the internal
and published ranges must agree. Read-only dedicated-server assets may be
shared when they are mounted separately from the writable terrain layers and
configuration.

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

## Shared gameplay actions

Autonomous actors must use the same character-bound action services as client
request handlers. Production refining, prototyping, research, calibration-line
creation, and mass production expose typed quote and execute operations through
this boundary. The P31 `productionResearchQuery`, `productionResearch`,
`productionCPRGInfo`, `productionLineCalibrate`,
`productionQueryLineNextRound`, and `productionLineStart` handlers are protocol
adapters over those services and retain their existing response dictionaries.
The shared services retain normal facility access, docking, technology,
material, slot, credit, personal/corporation wallet, production-time, and
transaction rules; the autonomous source marker is audit metadata and grants
no privileges.

The recipe catalog and industry planner are strategic, read-only tools. A plan
does not reserve materials or authorize production. Before every production
step, an actor must obtain a character-specific quote and execute it through
the shared action service. This separation lets long-term planners be replaced
or extended without creating a second, privileged gameplay implementation.

The opt-in `manufacturer` behavior is the first narrow execution loop. It
requires a dedicated character, a target definition, quantity, and mill
facility. Its durable row in `dbo.ai_industry_goal` records the original target
inventory, selected line, running production, phase, and missing-component
procurement goals. On every retry it inspects the public terminal container and
the character's existing running production before acting. A matching running
job wins over starting another one, so a restart between production commit and
goal-state persistence cannot duplicate work.

The controller can use a character-owned calibration program to create its
line. When research and prototype facilities are configured, it can first
produce the required prototype from quoted components, then consume that
prototype and a matching character-owned research kit to create the program.
It recognizes matching prototype, research, and mass-production jobs before
acting, so a restart between an action commit and goal-state persistence cannot
duplicate work. Prototype, research, calibration, and mass production are each
quoted immediately before execution through their shared gameplay action
service.

The resulting line is used for one normal mass-production cycle at a time.
The controller compares the fresh character-specific quote with real public-
container inventory. Missing research inputs, calibration programs, materials,
docking/facility access, slots, money, or another gameplay rejection become
persisted wait states and procurement goals; no item, credit, line, research
point, unlock, or completion is granted. The strategic recursive planner may
supply procurement leaves, but it never authorizes execution. Apply
`database/overlays/006_ai_industry_goal.sql` and
`database/overlays/007_ai_industry_lifecycle.sql`, and
`database/overlays/008_ai_industry_refinery.sql` before enabling the behavior.

```json
{
  "CharacterId": 123,
  "Enabled": true,
  "Behavior": "manufacturer",
  "Manufacturer": {
    "TargetDefinition": 100,
    "Quantity": 1,
    "MillFacilityEid": 456,
    "ResearchFacilityEid": 457,
    "PrototypeFacilityEid": 458,
    "RefineryFacilityEid": 459,
    "UseCorporationWallet": false,
    "RetrySeconds": 30,
    "Procurement": {
      "Enabled": true,
      "MaximumPurchaseQuantity": 100,
      "MaximumUnitPrice": 25.0,
      "WalletReserve": 10000.0
    }
  }
}
```

`ResearchFacilityEid`, `PrototypeFacilityEid`, and `RefineryFacilityEid` are
optional and default to zero. When the planner's next ordered step is refining,
a configured refinery is quoted for the exact character and bounded required
amount immediately before the shared refine action is executed. The synchronous
inventory result is authoritative, so restart replanning neither forgets the
output nor repeats already satisfied work. With no refinery, the controller
waits and may procure the refined input through the normal market when
separately enabled. With no research facility, the controller waits for a
calibration program acquired through the normal economy. Manufactured
intermediates use the same prototype, research, calibration, and one-cycle mill
path as the final target. After each job the controller replans from real
inventory and chooses the next ordered definition, while a matching running job
wins over any retry after restart. With no prototyper, it waits for the required
prototype or item. Configured facilities must be accessible from the
character's current docking base. The ordinary action services enforce
that relationship, tech-tree unlocks, slots, materials, wallets, time, and all
other character-specific production rules. A missing unlock is a wait state;
the controller never spends or grants research points.

Local procurement is separately opt-in. When enabled, the controller attempts
at most one bounded purchase per retry from the lowest eligible sell order in
the character's current market. It never sees remote offers, buys from itself,
exceeds `MaximumUnitPrice`, exceeds `MaximumPurchaseQuantity`, or spends below
`WalletReserve`. Personal or corporation wallet selection follows
`UseCorporationWallet` and the same market action used by clients. The public
container remains authoritative after a crash: the next character-specific
quote recalculates what is still missing before another purchase. No offer,
insufficient spendable credit, restricted corporation orders, and market races
become ordinary wait states rather than grants or retries outside the normal
market transaction.

Changing `TargetDefinition`, `Quantity`, or a configured production facility
does not overwrite an existing durable goal. The controller enters
`BlockedConfiguration` until the old goal is deliberately reviewed. This
prevents a configuration edit from silently forgetting work. Procurement caps
are operating policy and may be tightened without replacing the goal.
Do not assign `manufacturer` to a miner, trader, mentor, system agent, or other
owned character merely to exercise the loop; provision and document a
dedicated normal character instead.

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

World-teleport use is also a shared authenticated action. The stock-client
handler is only a protocol adapter; the action resolves the actor's real
in-world player and retains channel activity and validity, source range,
teleport sickness, PvP restrictions, mobile-teleport ownership and gang rules,
transactional movement, mission progress, cooldown, and activation behavior.
Autonomous callers receive no special destination or movement facility.

For later hauling and regional trading, the autonomous layer can copy the same
listable teleport descriptions returned to clients, including public source
column position and range. Its route policy considers only active, valid,
listable inter-zone channels and deterministically selects a shortest sequence
of channel IDs. The reusable world-travel controller advances toward each
source in deterministic 36-unit local legs, using the normal bounded A* and
movement-input service for every leg. If a leg is blocked, bounded alternate
headings fan out around the obstacle; the controller never writes position or
speed. At the column it waits for normal teleport sickness to expire, invokes
the shared audited action, waits for ordinary asynchronous zone entry, then
recomputes the remaining public route from the zone actually reached. A
20-second transition grace prevents headless restart recovery from racing that
zone entry, while retaining recovery if entry genuinely fails. Career policy
still chooses the economic destination and decides how to handle a reported
unavailable, blocked, or timed-out route.

Regional price knowledge is also actor-local and observation-driven. While
docked, a behavior may remember the local market identity, base and zone,
lowest eligible sell order, highest eligible buy order, visible quantities,
and local trade average for configured definitions. The bot cannot refresh or
query another market remotely through this service. Trade planning rejects
expired observations and same-zone pairs, then bounds a prospective batch by
the remembered source and destination depth, wallet budget, robot free volume,
and a configured maximum. The result is only an expected gross opportunity;
execution must travel normally and re-observe the source offer before spending
NIC. Each character has separate memory in `dbo.ai_market_memory`, created by
`database/overlays/004_ai_market_memory.sql`; corporation knowledge sharing is
a later explicit social policy rather than an implicit global cache.

The first regional executor is separately opt-in as `trader`. Its configured
market base EIDs are explicit route knowledge, analogous to destinations a
player has chosen to visit; they do not reveal prices. Commodity values are
entity-default names, not numeric server internals. Apply
`database/overlays/005_ai_trade_state.sql` before enabling it. The overlay
creates durable shipment state and updates the earlier market-memory location
constraint so zone ID `0`, which is valid in P31, can be observed. A minimal
shape is:

```json
{
  "CharacterId": 123,
  "Enabled": true,
  "Behavior": "trader",
  "RecoveryRevision": 0,
  "Trader": {
    "Throttle": 0.45,
    "DockedDwellSeconds": 10,
    "MaximumObservationAgeMinutes": 120,
    "MinimumUnitProfit": 1.0,
    "MinimumMargin": 0.05,
    "MaximumQuantity": 100,
    "WalletReserve": 10000.0,
    "OrderDurationHours": 24,
    "MarketBaseEids": [111111, 222222],
    "Commodities": ["def_titan"]
  }
}
```

The trader visits the least-recently observed configured market through normal
undock, movement, public teleport, approach, and dock actions. At each terminal
it records only the local order books for its configured commodities. Once its
own fresh observations reveal a cross-zone opportunity, it returns to the
source and refreshes that offer before spending NIC. Quantity is bounded again
by the live sell order and robot capacity; `WalletReserve` is excluded from its
planning budget.

A successful purchase lands in the public terminal container and is relocated
into the active robot exactly as for a player. Purchase, relocation, and the
new `dbo.ai_trade_state` row are committed in one ambient transaction. The row
stores the remaining quantity, actual unit cost, and source and destination;
after a restart or human-session suspension, the same shipment remains the
actor's priority. At the destination the bot refreshes the local quote and
either fulfills an eligible best bid or creates a normal sell order at the
higher of its absolute-profit and margin floors. Sale/listing and durable
quantity reduction are likewise one transaction, preventing replay after a
restart. Normal credit, fees, order slots, ownership, saleability, cargo loss,
teleport restrictions, terrain, collision, speed, docking range, and robot
recovery remain authoritative.

The initial executor carries one shipment at a time. It does not share market
memory, cancel old orders, respond tactically to threats, or manufacture an
unprofitable route. Those are later career and corporation policies, not
privileges hidden in transport.

The manufacturing foundation exposes `IProductionRecipeCatalog`, a read-only
projection over the same initialized `components`, `prototypes`,
`itemresearchlevels`, and entity-default caches used by the production engine.
Each recipe retains its output batch size, prototype and calibration metadata,
and the existing facility-specific component applicability rules. In
particular, robot shards remain prototype inputs and are not incorrectly
multiplied into every mill batch. Basic commodities use refinery per-unit output
semantics; definitions without a supported intended route remain external
requirements rather than exploiting the overly broad low-level refine entry
point.

`IAutonomousIndustryPlanner` recursively expands a requested output quantity
into deterministic, dependency-ordered nominal refinery and mill steps. It
consumes a supplied inventory snapshot first, reuses excess output from whole
batches across sibling requirements, aggregates remaining procurement leaves,
and reports produced surplus. Cycles, arithmetic overflow, excessive depth,
and excessive graph size return bounded failures without executable partial
steps. The result is strategic planning data only: it does not check a
character's unlocks, quote material efficiency, reserve items, spend credits,
create a calibration line, or start production. Those remain a later shared
action-service boundary so an autonomous manufacturer must satisfy the same
facility, inventory, research, time, and wallet rules as a player.

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
      "BuyFromMarket": false,
      "TileProbeReserve": 64,
      "MiningChargeReserve": 500,
      "MaximumPurchaseQuantity": 500,
      "MaximumUnitPrice": 0.0,
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
covers three rings. Survey spacing is constrained so every adjacent route leg
stays within the navigator's safe planning range.

Survey progress is retained across ordinary dock, probe resupply, and redeploy
trips, so a fitted module with fewer charges than `MaxSurveySites` eventually
continues into the outer rings instead of rescanning the inner ring forever.
After redeploy, the miner consumes no probe at the terminal: it replays the
persisted sequence of positions it physically reached on prior trips, with
every waypoint subject to normal pathfinding and movement, and resumes scanning
only at the persisted frontier. Because normal undocking can choose a different
side of a large terminal, the outbound trip first walks short perimeter legs
around the terminal to the original survey origin instead of crossing the
terminal or treating the new spawn as a new origin. Positions actually reached
during that deployment separately become its return breadcrumbs. A restart
during transit conservatively returns the actor to base before replaying the
durable route on its next trip.
Progress resets after a deposit observation or bounded survey exhaustion.
Return trips first retrace the survey positions the actor actually reached, in
reverse order, so difficult terrain is exited along demonstrated paths. If a
breadcrumb leg still remains physically stuck after the follower's internal
replans, recovery first takes a short normal-movement escape step on the
actor's observed side of the terminal and then targets the outer docking
radius. Subsequent attempts rotate through deterministic escape and approach
points instead of retrying the identical endpoint indefinitely.

For wider configured surveys, each adjacent spiral leg—not the total distance
from the terminal—is constrained to the navigator's safe range. The original
origin, next site, and reached route persisted in `dbo.ai_actor_work_state`
survive dock, resupply, and restart. Older rows without a reached route fall
back once to deterministic candidate reconstruction; successful traversal then
records only the positions normal pathfinding actually reached. The route
contains no mineral result or hidden terrain-layer data.

Mining progress requires `dbo.ai_actor_work_state`, created by
`database/overlays/002_ai_actor_work_state.sql` and extended by
`database/overlays/003_ai_actor_survey_route.sql`. Each transition records the
base, zone, return origin, observed target, material, next survey site, and the
ordered coordinates the actor already reached through normal movement. A
restart resumes a target only when those facts still match; otherwise the actor
discards stale coordinates and uses normal base recovery. Matching survey
progress remains available after a server restart. The separate
`dbo.ai_actor_state` robot identity guard remains authoritative.

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
after `RetrySeconds`. It never creates or silently refills ammunition.

`BuyFromMarket` optionally extends resupply through the normal local
`marketBuy` and `relocateItems` actions. The miner recognizes definitions only
from compatible ammunition already loaded or present in its own terminal and
robot containers. It buys the lowest eligible local sell order up to the
configured per-type reserve, finite offer quantity, `MaximumPurchaseQuantity`,
and `MaximumUnitPrice`, using its personal wallet. The price cap is checked
again while the selected order is locked for purchase, so a changed offer
cannot bypass it. The purchase first lands in the actor's public terminal
container and is then moved into robot cargo, just as it is for a player.
Market access, corporation restrictions, available credit, order quantity,
container capacity, and all transaction logging remain authoritative. With
buying disabled—the compatibility default—resupply remains cargo-only.
An interrupted purchase can resume by moving its matching terminal stack on a
later tick, but only when the complete stack still fits under the configured
cargo reserve; an oversized stack remains in terminal storage.

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
