# Syndicate Field Certification — Server Foundation

## Current-system audit

The stock English Rookie Checklist contains 97 trigger titles across four chapters:

- Introduction
- The essentials
- Advanced functions
- Complex tasks

Most triggers teach individual windows or gestures. The sequence includes opening panels,
dragging specific items, fitting and reloading, movement, teleporting, combat, assignments,
scanning, mining, harvesting, artifacts, refining, recycling, research, prototyping, reverse
engineering, mass production, and trials of three faction robots.

This is too long for the intended immediate-action onboarding. It also creates an authority gap:

- checklist progress is evaluated by the stock client;
- the server does not receive the current checklist task or completed chapter count while the
  player is training;
- when a training-exit teleport is used, the client submits `rewardLevel`;
- the server clamps that value to 0–4 and uses it for cumulative NIC/items, but it does not verify
  the submitted level against server-owned completion state.

Consequently, neither Mentor nor deterministic recovery logic can reliably know the current
Rookie Checklist step. The replacement must not treat the legacy checklist as authoritative.

## Reusable server capabilities

The existing mission engine already persists and exposes objective types suitable for a first
certification:

- reach position;
- lock unit;
- kill definition;
- loot/fetch/submit item;
- use switch or item supply;
- dock in;
- teleport;
- scan unit/mineral/container;
- drill mineral and harvest plant;
- find artifact;
- prototype, research, and mass produce.

These objectives are already visible to the Mentor through its read-only mission-state reader.
Using them gives onboarding, Mentor explanations, recovery, and later analytics one server-owned
source of truth.

## Recommended first playable vertical slice

Implement one short, persisted mission-chain prototype before replacing the entire checklist:

1. **Deploy** — activate the supplied Arkhe and leave the training terminal.
2. **Move and interact** — reach a nearby marker and activate one obvious switch.
3. **Target acquisition** — travel to the existing shooting range and primary-lock a tutorial
   target.
4. **First combat** — destroy one tutorial target.
5. **Immediate reward** — grant a small, explicit reward from server-verified completion.
6. **Choice** — offer graduation or an optional activity certification.

The first human test should measure time to deploy, time to first combat, whether the next action is
clear without reading a long page, and whether Mentor can accurately state the current objective.

## Implementation boundary

The next code/content change needs a deliberate persistence choice:

- Prefer the existing mission system and a new content patch for the certification definitions.
- Do not add an in-memory-only onboarding state; it would be lost on restart and could disagree
  with rewards.
- Do not modify migration 005.
- Do not trust the client-provided legacy reward level for new certification rewards.
- Keep reward granting deterministic and outside LLM control.

No database, live configuration, container data, navigation implementation, or market
implementation was changed during this audit.

## Implemented prototype: automatic first assignment

The first playable increment reuses the stock `mission_tutorialchecklist_transport` mission already
present at the training hub. New training characters are enrolled after character creation commits.
The normal mission engine remains the source of truth for persistence, active objective order, and
the 10,000 NIC completion reward.

Before starting it, the server verifies the content contract by stable mission name:

- location is training zone 45;
- reward is positive;
- objectives are exactly `reach_position`, `use_itemsupply`, and `submit_item` in that order.

If the installed content does not match, character creation still succeeds and the server records
`field_certification_skipped` with the reason. Enrollment runs asynchronously and does not extend
the account/character creation transaction. No database schema or content migration is required.

The client compatibility layer treats this validated assignment as the authoritative server-owned
entry experience while preserving the minimum stock-client bootstrap:

- the character's real race/school training state remains unchanged on the server;
- before first login, the server initializes the stock client's persisted Rookie Checklist trigger
  map as complete and acknowledges its Welcome screen. This silently unlocks ordinary UI controls
  without presenting or requiring the legacy checklist;
- the client-facing profile reports the character as outside legacy training while the real
  race/school values and server-side training restrictions remain unchanged;
- character selection refreshes the running-assignment list after a two-second delay, with
  bounded retries so rapid selection cannot outrun asynchronous enrollment, but does not force a
  modal mission-start window over the client's Welcome flow;
- Mentor announces the server-tracked first-shift assignment privately;
- the legacy tutorial welcome mail and Help-channel announcement are no longer sent to newly
  created training characters; and
- the particular starter Arkhe receives a persisted per-inventory capacity reservation calculated
  from its current load and the validated mission item volume. The stock mission requires a
  50-volume container while the Arkhe normally has 3 cargo capacity; no global robot definition or
  database content is changed;
- that Arkhe is activated during character creation, so the first job starts with an appropriate
  robot already assigned instead of depending on the player finding and activating it through a
  still-locked interface; and
- Mentor receives the localized server-owned instruction for every live mission objective and has
  general Item Supply / Submit Item interaction semantics available, improving guidance beyond this
  one assignment; and
- after each committed objective completion, Mentor sends a short deterministic handoff for the
  next action. These notifications are driven by persisted mission progress and never depend on an
  LLM inferring whether the player actually completed a step.

The closed client still opens its Rookie Checklist panel for a real training character in the
virtual training zone. No separate persisted "window open" setting exists: the panel is client
behavior tied to training identity. The compatibility state renders every legacy task complete and
unlocks the normal controls, so the player can close this cosmetic panel and use the server-owned
assignment. Spoofing race or school values merely to suppress the panel is intentionally avoided
because those values affect faction-sensitive client behavior.

The current stock assignment consumes its mission cargo at submission, but its Arkhe is the
character's ordinary starter robot. It must not be reclaimed after this single delivery because
doing so would strand the player. A purpose-built certification chain should persistently mark
loaned company assets and reclaim them only when it grants the player's replacement equipment or
the character graduates.

The second playable increment is an authored `Target Acquisition` combat-training assignment in
the same virtual training zone. Overlay `006_syndicate_field_certification_combat.sql`:

- leaves migration 005 untouched and changes no schema;
- chains the new unique assignment from successful Transport Training completion;
- verifies that the stock zone-45 training hub and persistent training Scarab flock exist before
  changing content;
- teaches reach-position, primary target locking, and a single verified kill in that order; and
- grants 15,000 NIC from server-verified mission completion.

The configured `lock_unit` engine path now supports persistent NPC definitions and bounded target
positions, matching the established configured-kill semantics. Quantity-only lock targets retain
strict mission-GUID ownership for dynamically spawned NPCs. The certification deliberately uses
the persistent shooting-range Scarabs so an active assignment remains recoverable across server
restarts. Server-owned localization tokens are added to the sign-in custom dictionary in memory;
neither packaged client resources nor mounted runtime configuration are edited.

If either replacement mission is missing or its location, reward, or objectives change, its
presentation/enrollment contract fails closed. Graduation and optional activity certifications
remain subsequent slices.
