// ---- Types (mirror the backend DTOs, camelCased by ASP.NET) ----
export interface Side {
  teamId: number | null
  code: string | null
  name: string
  flag: string | null
  isPlaceholder: boolean
}

export interface Match {
  id: number
  stage: string
  label: string
  kickoffUtc: string
  status: 'Scheduled' | 'InPlay' | 'Finished'
  home: Side
  away: Side
  homeScore: number | null
  awayScore: number | null
  minute: string | null
}

export interface TeamBoard {
  id: number
  code: string
  name: string
  flag: string
  group: string
  status: 'Alive' | 'Eliminated'
  eliminatedStage: string | null
  isChampion: boolean
  liveMatch: Match | null
  nextMatch: Match | null
}

export interface PlayerBoard {
  id: number
  name: string
  aliveCount: number
  teams: TeamBoard[]
}

export interface Champion {
  teamId: number
  code: string
  name: string
  flag: string
  players: string[]
}

export interface Board {
  entryFee: number
  playerCount: number
  potTotal: number
  currency: string
  lastSyncUtc: string | null
  dataProvider: string
  champion: Champion | null
  liveMatches: Match[]
  players: PlayerBoard[]
}

export interface Team {
  id: number
  fifaCode: string
  name: string
  flagEmoji: string
  groupName: string
  status: string
  isChampion: boolean
  manualOverride: boolean
}

export interface PlayerRow {
  id: number
  name: string
  teamIds: number[]
}

// ---- Fetch helpers ----
async function getJson<T>(url: string): Promise<T> {
  const res = await fetch(url)
  if (!res.ok) throw new Error(`${res.status} ${res.statusText}`)
  return res.json()
}

export const getBoard = () => getJson<Board>('/api/board')
export const getTeams = () => getJson<Team[]>('/api/teams')
export const getPlayers = () => getJson<PlayerRow[]>('/api/players')
export const getMatches = () => getJson<Match[]>('/api/matches')

// ---- Admin (token sent as X-Admin-Token) ----
async function adminFetch(token: string, url: string, method: string, body?: unknown) {
  const res = await fetch(url, {
    method,
    headers: {
      'X-Admin-Token': token,
      ...(body ? { 'Content-Type': 'application/json' } : {}),
    },
    body: body ? JSON.stringify(body) : undefined,
  })
  if (res.status === 401) throw new Error('Unauthorized — check the admin token')
  if (!res.ok) {
    const text = await res.text().catch(() => '')
    throw new Error(text || `${res.status} ${res.statusText}`)
  }
  return res.status === 204 ? null : res.json().catch(() => null)
}

export const admin = {
  addPlayer: (t: string, name: string) => adminFetch(t, '/api/admin/players', 'POST', { name }),
  deletePlayer: (t: string, id: number) => adminFetch(t, `/api/admin/players/${id}`, 'DELETE'),
  addAssignment: (t: string, playerId: number, teamId: number) =>
    adminFetch(t, '/api/admin/assignments', 'POST', { playerId, teamId }),
  removeAssignment: (t: string, playerId: number, teamId: number) =>
    adminFetch(t, `/api/admin/assignments?playerId=${playerId}&teamId=${teamId}`, 'DELETE'),
  setTeamStatus: (
    t: string,
    teamId: number,
    payload: { status?: string; eliminatedStage?: string; isChampion?: boolean; clearOverride?: boolean },
  ) => adminFetch(t, `/api/admin/teams/${teamId}/status`, 'PUT', payload),
  sync: (t: string) => adminFetch(t, '/api/admin/sync', 'POST'),
  updateSettings: (
    t: string,
    payload: { entryFee?: number; livePollSeconds?: number; idlePollSeconds?: number; adminToken?: string; dataProvider?: string },
  ) => adminFetch(t, '/api/admin/settings', 'PUT', payload),
}
