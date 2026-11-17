import { createRoot } from 'react-dom/client'
// T-015-01 (F-TRF-015): token definitions first — must precede the usage rules in index.css.
import './styles/tokens.css'
import './index.css'
import './features/recipient/recipient.css'
import './features/errors/errorMap.css'
import './core/i18n' // T-014-01: i18next init before first render (synchronous on import)
import App from './App.tsx'

// T-003-09 dev-only: StrictMode double-mount fires two parallel GETs that race the singleton WaDbContext (pre-existing since T-002-02) → EF concurrency 500. No-op in prod builds; restore once DI is scoped.
createRoot(document.getElementById('root')!).render(<App />)
