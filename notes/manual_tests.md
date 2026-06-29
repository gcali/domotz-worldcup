# Manual tests — worldcup (Office World Cup 2026 Sweepstake)

> Pre-merge gate for the sweepstake webapp. Tick each box as it's actually
> exercised. "Dev" = `dotnet run` + `npm run dev`; "Container" = `docker run`.

## Pre-requisites

- [ ] Backend builds (`dotnet build`) and frontend builds (`npm run build`) clean
- [ ] (Optional) an api-sports.io key in `ApiFootball__ApiKey` for live-score checks
- [ ] Admin token known (`Admin__Token` env var, default `changeme`)

---

## 1. Seed & schedule load

**Goal:** first boot pulls the real tournament data.

- [ ] On first run the DB seeds **48 teams** across **12 groups** (A–L)
- [ ] `/api/matches` returns **104** fixtures; group matches have real teams, knockout
      slots show placeholders (e.g. `2A`, `W101`) flagged `isPlaceholder`
- [ ] Kickoff times are correct UTC (e.g. opener `13:00 UTC-6` → `19:00Z`)

## 2. Board renders (and on mobile)

- [ ] `/` shows the pot (💶 €fee × players), player count, and "updated …" freshness
- [ ] Player cards list owned teams with flags and an "N alive" pill
- [ ] Each entry shows the **last match result** (W/D/L + score + opponent) and the
      **next fixture** (opponent + kickoff); an undecided knockout opponent shows **TBD**
- [ ] Layout is usable at phone width (single column, no overflow)

## 3. Admin auth gate

- [ ] `POST /api/admin/*` without / with a wrong `X-Admin-Token` → **401**
- [ ] With the correct token the admin page can add players and assign teams

## 4. Players & assignments

- [ ] Add players; set one team each; board reflects ownership and pot grows
- [ ] Setting a new team for a player replaces their previous pick (no duplicates)
- [ ] A team already picked by another player shows the current owner in the picker
      but is still selectable (two players CAN share the same team)
- [ ] Removing an assignment / player updates the board

## 4b. Bet-state export / import (admin)

- [ ] **Export JSON** downloads `sweepstake-bets.json` = `{ players: [{ name, teams: [FIFA codes] }] }`
- [ ] **Import JSON** of a dump prompts for confirmation, then completely replaces all
      players + picks (board reflects the imported state)
- [ ] A round-trip (export → import the same file) leaves the bet state unchanged
- [ ] Import with an unknown team code or empty player name → rejected (400) and the
      existing state is left untouched (atomic)
- [ ] Export/import without a valid admin token → 401

## 5. Elimination logic

- [ ] Override a team → **Eliminated**: it shows ❌ + stage, drops out of its owner's
      "alive" count, and is struck through
- [ ] (With results) a knockout loser auto-flips to eliminated after a finished match
- [ ] Once the Round of 32 is fully drawn, group-stage non-qualifiers (e.g. a team that
      finished bottom of its group) show **Eliminated · "Group stage"**
- [ ] Re-syncing after the knockout bracket resolves does **not** duplicate fixtures:
      `/api/matches` stays at **104** (16 r32 / 8 r16 / 4 qf / 2 sf / 1 third / 1 final),
      and stale placeholder slots (`2A`, `3A/B/C/D/F`) are pruned, not left alongside the
      resolved fixtures
- [ ] **Clear override** → team returns to its auto-derived status on recompute

## 6. Champion & payout

- [ ] Crown a team **Champion** (or let the final resolve): champion banner appears
      naming the owning player(s) and the €pot they take
- [ ] Owner's card is highlighted and sorts to the top

## 7. Live scores (needs api-football key)

- [ ] During a live match the board shows in-progress **score + minute** and a "Live now"
      strip; team badge shows ⚽ LIVE
- [ ] Poller switches to live cadence during a match and backs off when idle
- [ ] `ApiCallBudget` caps daily api-football calls (log warns when exhausted)

## 8. Container

- [ ] `docker build -t worldcup .` succeeds
- [ ] `docker run -p 8080:8080 …` serves the board at `/` and the API at `/api/*`
- [ ] SQLite persists across restarts when `/data` is a volume

---

## Roll-back plan

- It's a standalone app: stop the container / dev servers. Deleting the SQLite file
  (`worldcup.db` or the `/data` volume) resets all state and re-seeds on next boot.
