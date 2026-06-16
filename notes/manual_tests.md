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
- [ ] Layout is usable at phone width (single column, no overflow)

## 3. Admin auth gate

- [ ] `POST /api/admin/*` without / with a wrong `X-Admin-Token` → **401**
- [ ] With the correct token the admin page can add players and assign teams

## 4. Players & assignments

- [ ] Add players; set one team each; board reflects ownership and pot grows
- [ ] Setting a new team for a player replaces their previous pick (no duplicates)
- [ ] Teams already taken by another player are disabled in the picker
- [ ] Removing an assignment / player updates the board

## 5. Elimination logic

- [ ] Override a team → **Eliminated**: it shows ❌ + stage, drops out of its owner's
      "alive" count, and is struck through
- [ ] (With results) a knockout loser auto-flips to eliminated after a finished match
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
