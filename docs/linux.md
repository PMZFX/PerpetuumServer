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
      "Patrol": {
        "Radius": 14,
        "Throttle": 0.45,
        "DockedDwellSeconds": 15,
        "FieldDwellSeconds": 2
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
