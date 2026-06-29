import { useEffect, useState } from 'react'
import { getBoard, type Board as BoardData, type Match, type TeamBoard } from './api'

const REFRESH_MS = 30_000

function timeAgo(iso: string | null): string {
  if (!iso) return 'never'
  const secs = Math.max(0, (Date.now() - new Date(iso).getTime()) / 1000)
  if (secs < 60) return 'just now'
  if (secs < 3600) return `${Math.floor(secs / 60)}m ago`
  return `${Math.floor(secs / 3600)}h ago`
}

function initials(name: string): string {
  const parts = name.trim().split(/\s+/)
  return parts.slice(0, 2).map(p => p[0]?.toUpperCase() ?? '').join('') || '?'
}

function nameHash(name: string): number {
  let h = 0
  for (const c of name) h = (h * 31 + c.codePointAt(0)!) % 997
  return h
}

function kickoff(iso: string): string {
  return new Date(iso).toLocaleString(undefined, {
    weekday: 'short', day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit',
    hour12: false,
  })
}

function scoreText(m: Match): string {
  const h = m.homeScore ?? 0
  const a = m.awayScore ?? 0
  return `${h}–${a}`
}

function LiveLine({ m }: { m: Match }) {
  return (
    <div className="live-line">
      <span className="ball">⚽</span>
      <span className="t">{m.home.flag} {m.home.name}</span>
      <strong className="sc">{scoreText(m)}</strong>
      <span className="t">{m.away.name} {m.away.flag}</span>
      <span className="min">{m.minute ?? 'LIVE'}</span>
    </div>
  )
}

function opponent(m: Match, teamId: number) {
  return m.home.teamId === teamId ? m.away : m.home
}

// A finished match seen from one team's side: outcome + "2–1 vs 🇷🇸 Serbia".
function result(m: Match, teamId: number): { outcome: 'W' | 'D' | 'L'; text: string } {
  const home = m.home.teamId === teamId
  const us = (home ? m.homeScore : m.awayScore) ?? 0
  const them = (home ? m.awayScore : m.homeScore) ?? 0
  const opp = opponent(m, teamId)
  const outcome = us > them ? 'W' : us < them ? 'L' : 'D'
  return { outcome, text: `${us}–${them} vs ${opp.flag ? `${opp.flag} ` : ''}${opp.name}` }
}

// Opponent label for an upcoming fixture — TBD while the other slot isn't decided.
function fixtureOpponent(m: Match, teamId: number): string {
  const opp = opponent(m, teamId)
  return opp.isPlaceholder ? 'TBD' : `${opp.flag ? `${opp.flag} ` : ''}${opp.name}`
}

function MatchLines({ team }: { team: TeamBoard }) {
  const { lastMatch, nextMatch } = team
  if (!lastMatch && !nextMatch) return null
  const r = lastMatch ? result(lastMatch, team.id) : null
  return (
    <div className="match-lines">
      {lastMatch && r && (
        <div className="ml">
          <span className={`res ${r.outcome}`}>{r.outcome}</span>
          <span className="ml-text">{r.text}</span>
          <span className="ml-when">{lastMatch.label}</span>
        </div>
      )}
      {nextMatch && (
        <div className="ml">
          <span className="ml-tag">NEXT</span>
          <span className="ml-text">vs {fixtureOpponent(nextMatch, team.id)}</span>
          <span className="ml-when">{kickoff(nextMatch.kickoffUtc)}</span>
        </div>
      )}
    </div>
  )
}

function StatusChip({ team }: { team: TeamBoard | null }) {
  if (!team) return <span className="status-chip none">no team</span>
  if (team.isChampion) return <span className="status-chip champ">🏆 winner</span>
  if (team.status === 'Eliminated') return <span className="status-chip out">out</span>
  if (team.liveMatch) return <span className="status-chip live">● live</span>
  if (team.inProgressMatch) return <span className="status-chip live">● playing</span>
  return <span className="status-chip in">in it</span>
}

const STAGE_SHORT: Record<string, string> = {
  r32: 'R32', r16: 'R16', qf: 'QF', sf: 'SF', third: '3rd place', final: 'Final',
}

// Subtitle = where the team is now in the tournament: the round of its current/next match
// (or the last one it played), falling back to the group name while still in the group stage.
function teamStage(team: TeamBoard): string {
  if (team.isChampion) return 'Champion'
  const active = team.inProgressMatch ?? team.liveMatch ?? team.nextMatch ?? team.lastMatch
  if (!active || active.stage === 'group') return team.group
  return STAGE_SHORT[active.stage] ?? team.group
}

function TeamHero({ team }: { team: TeamBoard }) {
  const cls = team.isChampion
    ? 'team-hero champ'
    : team.status === 'Eliminated'
      ? 'team-hero out'
      : team.liveMatch
        ? 'team-hero live'
        : 'team-hero'
  return (
    <div className={cls}>
      <span className="flag">{team.flag}</span>
      <div className="t-info">
        <span className="t-name">{team.name}</span>
        <span className="t-group">{teamStage(team)}</span>
      </div>
      <div className="t-state">
        {team.isChampion ? (
          <span className="badge gold">🏆 World Champions</span>
        ) : team.status === 'Eliminated' ? (
          <span className="badge out">❌ {team.eliminatedStage ?? 'Out'}</span>
        ) : team.liveMatch ? (
          <span className="badge live">⚽ {scoreText(team.liveMatch)} · {team.liveMatch.minute ?? 'LIVE'} vs {opponent(team.liveMatch, team.id).name}</span>
        ) : team.inProgressMatch ? (
          <span className="badge live">⚽ In progress · vs {opponent(team.inProgressMatch, team.id).name}</span>
        ) : (
          <span className="badge ok">🟢 In the running</span>
        )}
      </div>
    </div>
  )
}

export default function Board() {
  const [data, setData] = useState<BoardData | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let active = true
    const load = () => getBoard().then(d => { if (active) { setData(d); setError(null) } }).catch(e => active && setError(String(e)))
    load()
    const id = setInterval(load, REFRESH_MS)
    return () => { active = false; clearInterval(id) }
  }, [])

  if (error && !data) return <div className="page"><p className="error">Could not load the board: {error}</p></div>
  if (!data) return <div className="page"><p className="muted">Loading…</p></div>

  return (
    <div className="page">
      <header className="hero">
        <div className="hero-left">
          <h1>Office World Cup 2026</h1>
          <p className="tagline">⚽ Last team standing takes the pot</p>
          <p className="meta" title={`Source: ${data.dataProvider}`}>
            {data.playerCount} players · updated {timeAgo(data.lastSyncUtc)}
          </p>
        </div>
        <div className="pot-card" aria-label={`Prize pool: €${data.potTotal}`}>
          <span className="chest">💰</span>
          <div className="pot-info">
            <span className="pot-label">Prize pool</span>
            <span className="pot-value">€{data.potTotal}</span>
          </div>
        </div>
      </header>
      <div className="pitch-line" />

      {data.champion && (
        <div className="champion-banner">
          <div className="big">{data.champion.flag}</div>
          <div>
            <div className="winner">{data.champion.name} are World Champions!</div>
            <div className="takes">
              💶 <strong>{data.champion.players.join(' & ') || 'Nobody'}</strong> {data.champion.players.length > 1 ? 'split' : 'takes'} the €{data.potTotal} pot
            </div>
          </div>
        </div>
      )}

      {data.liveMatches.length > 0 && (
        <section className="live-now">
          <h2>⚽ Live now</h2>
          {data.liveMatches.map(m => <LiveLine key={m.id} m={m} />)}
        </section>
      )}

      <section className="grid">
        {data.players.length === 0 && (
          <p className="muted">No players yet. An admin can add players and assign teams at <code>/admin</code>.</p>
        )}
        {data.players.map(p => {
          const team = p.teams[0] ?? null
          return (
            <article
              className={`card ${team?.isChampion ? 'card-champ' : ''} ${team?.status === 'Eliminated' ? 'card-out' : ''}`}
              key={p.id}
            >
              <div className="card-head">
                <div className="who">
                  <span className={`avatar av-${nameHash(p.name) % 6}`}>{initials(p.name)}</span>
                  <h3>{p.name}</h3>
                </div>
                <StatusChip team={team} />
              </div>
              {team
                ? <><TeamHero team={team} /><MatchLines team={team} /></>
                : <p className="muted small">Waiting for the draw…</p>}
            </article>
          )
        })}
      </section>
    </div>
  )
}
