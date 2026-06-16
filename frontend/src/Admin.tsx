import { useEffect, useState, useCallback } from 'react'
import { admin, getBoard, getPlayers, getTeams, type Board, type PlayerRow, type Team } from './api'

const TOKEN_KEY = 'wc-admin-token'

export default function Admin() {
  const [token, setToken] = useState(localStorage.getItem(TOKEN_KEY) ?? '')
  const [teams, setTeams] = useState<Team[]>([])
  const [players, setPlayers] = useState<PlayerRow[]>([])
  const [board, setBoard] = useState<Board | null>(null)
  const [msg, setMsg] = useState<string | null>(null)
  const [err, setErr] = useState<string | null>(null)

  const teamsById = new Map(teams.map(t => [t.id, t]))
  const ownerByTeam = new Map<number, number>()
  players.forEach(p => p.teamIds.forEach(tid => ownerByTeam.set(tid, p.id)))

  const reload = useCallback(async () => {
    try {
      const [t, p, b] = await Promise.all([getTeams(), getPlayers(), getBoard()])
      setTeams(t); setPlayers(p); setBoard(b)
    } catch (e) { setErr(String(e)) }
  }, [])

  useEffect(() => { reload() }, [reload])

  async function run(action: () => Promise<unknown>, ok: string) {
    setMsg(null); setErr(null)
    try { await action(); setMsg(ok); await reload() }
    catch (e) { setErr(e instanceof Error ? e.message : String(e)) }
  }

  function saveToken() {
    localStorage.setItem(TOKEN_KEY, token)
    setMsg('Token saved')
  }

  // --- form state ---
  const [newPlayer, setNewPlayer] = useState('')
  const [assignPlayer, setAssignPlayer] = useState<number | ''>('')
  const [assignTeam, setAssignTeam] = useState<number | ''>('')
  const [statusTeam, setStatusTeam] = useState<number | ''>('')
  const [elimStage, setElimStage] = useState('Eliminated')
  const [fee, setFee] = useState('')
  const [livePoll, setLivePoll] = useState('')
  const [idlePoll, setIdlePoll] = useState('')
  const [newToken, setNewToken] = useState('')

  const groups = [...new Set(teams.map(t => t.groupName))].sort()

  return (
    <div className="page admin">
      <header className="hero">
        <h1>⚙️ Sweepstake admin</h1>
        <a className="pill" href="/">← back to board</a>
      </header>

      {msg && <p className="ok-msg">{msg}</p>}
      {err && <p className="error">{err}</p>}

      <section className="panel">
        <h2>Admin token</h2>
        <div className="row">
          <input type="password" placeholder="X-Admin-Token" value={token} onChange={e => setToken(e.target.value)} />
          <button onClick={saveToken}>Save</button>
          <button onClick={() => run(() => admin.sync(token), 'Sync triggered')}>↻ Sync results now</button>
        </div>
        <p className="muted small">Stored locally in your browser and sent as <code>X-Admin-Token</code>. Set the real value via <code>Admin:Token</code> / <code>ADMINTOKEN</code> on the server.</p>
      </section>

      <section className="panel">
        <h2>Players</h2>
        <div className="row">
          <input placeholder="New player name" value={newPlayer} onChange={e => setNewPlayer(e.target.value)} />
          <button disabled={!newPlayer.trim()} onClick={() => run(async () => { await admin.addPlayer(token, newPlayer.trim()); setNewPlayer('') }, 'Player added')}>+ Add player</button>
        </div>
        <ul className="admin-list">
          {players.map(p => (
            <li key={p.id}>
              <div className="pl-head">
                <strong>{p.name}</strong>
                <button className="link danger" onClick={() => run(() => admin.deletePlayer(token, p.id), 'Player removed')}>remove</button>
              </div>
              <div className="chips">
                {p.teamIds.length === 0 && <span className="muted small">no team yet</span>}
                {p.teamIds.map(tid => {
                  const t = teamsById.get(tid)
                  return (
                    <span className="chip" key={tid}>
                      {t ? `${t.flagEmoji} ${t.name}` : `#${tid}`}
                      <button className="x" title="remove" onClick={() => run(() => admin.removeAssignment(token, p.id, tid), 'Assignment removed')}>×</button>
                    </span>
                  )
                })}
              </div>
            </li>
          ))}
          {players.length === 0 && <li className="muted">No players yet.</li>}
        </ul>
      </section>

      <section className="panel">
        <h2>Team picks</h2>
        <p className="muted small">One team per player — setting a new team replaces the previous pick. Teams already taken are marked.</p>
        <div className="row">
          <select value={assignPlayer} onChange={e => setAssignPlayer(Number(e.target.value) || '')}>
            <option value="">Player…</option>
            {players.map(p => {
              const t = p.teamIds[0] !== undefined ? teamsById.get(p.teamIds[0]) : undefined
              return <option key={p.id} value={p.id}>{p.name}{t ? ` — ${t.flagEmoji} ${t.name}` : ''}</option>
            })}
          </select>
          <select value={assignTeam} onChange={e => setAssignTeam(Number(e.target.value) || '')}>
            <option value="">Team…</option>
            {groups.map(g => (
              <optgroup key={g} label={g}>
                {teams.filter(t => t.groupName === g).map(t => {
                  const owner = ownerByTeam.get(t.id)
                  const takenByOther = owner !== undefined && owner !== Number(assignPlayer)
                  const ownerName = takenByOther ? players.find(p => p.id === owner)?.name : null
                  return (
                    <option key={t.id} value={t.id} disabled={takenByOther}>
                      {t.flagEmoji} {t.name}{ownerName ? ` · ${ownerName}` : ''}
                    </option>
                  )
                })}
              </optgroup>
            ))}
          </select>
          <button
            disabled={!assignPlayer || !assignTeam}
            onClick={() => run(async () => {
              const current = players.find(p => p.id === Number(assignPlayer))
              for (const tid of current?.teamIds ?? [])
                await admin.removeAssignment(token, Number(assignPlayer), tid)
              await admin.addAssignment(token, Number(assignPlayer), Number(assignTeam))
            }, 'Team set')}
          >
            Set team
          </button>
        </div>
      </section>

      <section className="panel">
        <h2>Override team status</h2>
        <p className="muted small">Use when the API lags or for penalties it can't resolve. Manual overrides are kept and not touched by the auto-sync until cleared.</p>
        <div className="row">
          <select value={statusTeam} onChange={e => setStatusTeam(Number(e.target.value) || '')}>
            <option value="">Team…</option>
            {teams.map(t => <option key={t.id} value={t.id}>{t.flagEmoji} {t.name} — {t.status}{t.isChampion ? ' 🏆' : ''}{t.manualOverride ? ' (manual)' : ''}</option>)}
          </select>
          <input style={{ maxWidth: 160 }} placeholder="Eliminated at…" value={elimStage} onChange={e => setElimStage(e.target.value)} />
        </div>
        <div className="row">
          <button disabled={!statusTeam} onClick={() => run(() => admin.setTeamStatus(token, Number(statusTeam), { status: 'eliminated', eliminatedStage: elimStage }), 'Marked eliminated')}>❌ Eliminate</button>
          <button disabled={!statusTeam} onClick={() => run(() => admin.setTeamStatus(token, Number(statusTeam), { status: 'alive' }), 'Marked alive')}>🟢 Alive</button>
          <button disabled={!statusTeam} onClick={() => run(() => admin.setTeamStatus(token, Number(statusTeam), { isChampion: true }), 'Crowned champion')}>🏆 Champion</button>
          <button disabled={!statusTeam} className="link" onClick={() => run(() => admin.setTeamStatus(token, Number(statusTeam), { clearOverride: true }), 'Override cleared')}>clear override</button>
        </div>
      </section>

      <section className="panel">
        <h2>Settings</h2>
        <div className="row">
          <label>Entry fee €<input type="number" value={fee} placeholder={String(board?.entryFee ?? 5)} onChange={e => setFee(e.target.value)} /></label>
          <label>Live poll s<input type="number" value={livePoll} placeholder="90" onChange={e => setLivePoll(e.target.value)} /></label>
          <label>Idle poll s<input type="number" value={idlePoll} placeholder="3600" onChange={e => setIdlePoll(e.target.value)} /></label>
        </div>
        <div className="row">
          <input type="password" placeholder="Change admin token" value={newToken} onChange={e => setNewToken(e.target.value)} />
          <button onClick={() => run(async () => {
            await admin.updateSettings(token, {
              entryFee: fee ? Number(fee) : undefined,
              livePollSeconds: livePoll ? Number(livePoll) : undefined,
              idlePollSeconds: idlePoll ? Number(idlePoll) : undefined,
              adminToken: newToken || undefined,
            })
            if (newToken) { setToken(newToken); localStorage.setItem(TOKEN_KEY, newToken); setNewToken('') }
          }, 'Settings saved')}>Save settings</button>
        </div>
        <p className="muted small">Data source: <strong>{board?.dataProvider}</strong>. Pot: €{board?.potTotal}.</p>
      </section>
    </div>
  )
}
