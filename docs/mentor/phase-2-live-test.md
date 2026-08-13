# Mentor Phase 2 Live Test

## Live result — 2026-08-12

The stock client displayed the transient `MENTOR` tab for a new character. After changing
message notifications to use the requesting character as the private channel's valid sender,
the client displayed both submitted questions and asynchronous ARIA responses. Other channels
continued to accept messages normally. The player found a mixture of useful and unhelpful
diagnostic answers and approved continuing implementation. This passes the Phase 1 transport
gate and the Phase 2 client/context integration gate; answer quality continues in Phase 3.

This is the first checkpoint that requires a running server and an actual Perpetuum client.
It validates the client protocol behavior and the accuracy of live player and mission state;
neither can be fully proven by server unit tests.

## Setup

1. Build and run the server from the `perpetuum-ai/mentor-transport` worktree using the
   normal development environment.
2. Do not add a Mentor channel row or channel-member row to the database. The Mentor
   channel is intentionally virtual for this prototype.
3. Connect one client with a character that has an active robot and a tutorial or ordinary
   mission. If possible, connect a second client to verify response privacy.

## Channel behavior

1. Create/select a brand-new character and confirm that a highlighted `Mentor` tab appears
   automatically alongside the normal Corporation, Terminal, Help and Recruitment tabs.
2. Open the channel browser/list and confirm that `Mentor` is also present there.
3. Reconnect the character and confirm the channel appears again without manual joining.
4. Send a normal message in another channel and confirm its existing behavior is unchanged.
5. Send `mentor help` in Mentor and confirm the request acknowledgement is immediate and the
   question appears in the transcript and the labeled ARIA response arrives asynchronously.
6. With two clients connected, confirm each Mentor response is visible only to its requester.

## Authoritative context

Ask each question and compare the reply to current server/client state:

- `Where am I?`
- `What robot am I using?`
- `What mission am I on?`
- `What is my current objective?`
- `What is a Mesmer?`
- `What is definitely_not_a_real_item?`
- `Write me a poem`

Verify that:

- docked/undocked state, zone, coordinates, base and robot are correct;
- the selected mission and active objective match the client mission display;
- objective progress, required item/target, destination and coordinates are correct when present;
- entity matches use current server definitions and nonexistent definitions are not invented;
- unsupported questions receive an explicit uncertainty response;
- internal exceptions, SQL details and private account information never appear in chat.

## Failure and isolation checks

1. Send seven valid questions within one minute from one character. The seventh should receive
   the friendly rate-limit response while ordinary game/chat processing continues.
2. Disconnect immediately after submitting a Mentor question. The server must remain healthy
   and must silently drop the now-undeliverable targeted response.
3. Inspect server logs for `mentor request_id=...` entries with queue, outcome, latency and
   answer-length fields.
4. Confirm no channel, channel-member, migration, configuration or container state was created
   or changed by the Mentor prototype.

## Human sign-off needed

- The stock client renders the transient channel from its channel-list responses and keeps the
  tab after the character-selection notification sequence completes.
- The requester-backed sender ID plus the `[Syndicate] ARIA` label is readable and does not
  produce client errors. The requester-backed ID is a stock-client compatibility measure for
  this transient, private channel prototype.
- Server-derived definition names are understandable enough for the prototype.
- Mission/objective wording matches what a player sees and does not mislead.
