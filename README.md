# Office World Cup 2026 Sweepstake

A small webapp to track an office sweepstake for the 2026 FIFA World Cup: each player
picks one team, and the owner of the team that **wins the final takes the whole pot**. The centerpiece is a near-real-time **survival board** — who's still alive, whose
team is playing right now (live score + minute), and who's been knocked out.

- **Backend:** ASP.NET Core (.NET 10) minimal API + EF Core/SQLite + a background poller.
- **Frontend:** React + Vite + TypeScript (mobile-first), served by the backend in prod.
- **Data:** auto-ingested from a football API (admin can override any result).

## How it works

- **Teams** (48, in 12 groups) are seeded from the public-domain
  [`openfootball/worldcup.json`](https://github.com/openfootball/worldcup.json) feed.
- **Results** come from one active provider:
  - **api-football** (api-sports.io) when `ApiFootball:ApiKey` is set — includes **live
    in-play scores + match minute**. Free plan = 100 requests/day; an
    [`ApiCallBudget`](backend/Services/ApiCallBudget.cs) hard-caps daily usage and the
    [`PollerService`](backend/Services/PollerService.cs) only polls fast while a match is
    actually on (live mode) and sparsely otherwise (idle mode).
  - **openfootball** otherwise (no key; schedule + results, updated by maintainers, not
    live).
- **Survival logic** ([`StatusService`](backend/Services/StatusService.cs)) is derived
  purely from match data: a knockout loser is out at that stage; once the Round of 32 is
  fully drawn, any team that never reached it is out at the group stage; the final's
  winner is the champion. The admin can override any team's status (e.g. for penalties or
  API lag) and those overrides survive auto-recompute.

## Run locally (dev)

Two terminals:

```bash
# backend  → http://localhost:5180
cd backend
dotnet run

# frontend → http://localhost:5173 (proxies /api to :5180)
cd frontend
npm install   # first time
npm run dev
```

- Board: <http://localhost:5173/>
- Admin: <http://localhost:5173/admin>

The default admin token is `changeme` (override with the `Admin__Token` env var, or
`Admin:Token` in config). To enable live scores, set `ApiFootball__ApiKey`.

## Run as a container (prod-like)

The backend serves the built SPA from `wwwroot`, so it's a single image.

```bash
docker build -t worldcup .
docker run -p 8080:8080 \
  -e Admin__Token=your-secret \
  -e ApiFootball__ApiKey=your-api-sports-key \
  -v worldcup-data:/data \
  worldcup
```

Then open <http://localhost:8080> (admin at `/admin`). The SQLite DB lives in the `/data`
volume so it survives restarts.

## Configuration

| Setting | Env var | Default | Notes |
|---|---|---|---|
| Admin token | `Admin__Token` | `changeme` | gates all `/api/admin/*` |
| api-football key | `ApiFootball__ApiKey` | — | enables live scores |
| api-football league | `ApiFootball__LeagueId` | `1` | FIFA World Cup |
| api-football season | `ApiFootball__Season` | `2026` | |
| daily request cap | `ApiFootball__DailyBudget` | `90` | guards the 100/day free tier |
| openfootball URL | `OpenFootball__Url` | github raw | override to pin/mirror |
| DB connection | `ConnectionStrings__Default` | `Data Source=worldcup.db` | |

Live/idle poll cadence and the entry fee are editable at runtime from the admin page.

## Admin workflow

1. Open `/admin`, enter the admin token, **Save**.
2. Add players, then set each player's team (one each; picking again replaces it).
3. The board updates automatically as results come in. Use **Sync results now** to force
   a refresh, or the status overrides for anything the feed gets wrong.
