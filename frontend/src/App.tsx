import Board from './Board'
import Admin from './Admin'

export default function App() {
  const isAdmin = window.location.pathname.replace(/\/+$/, '') === '/admin'
  return isAdmin ? <Admin /> : <Board />
}
