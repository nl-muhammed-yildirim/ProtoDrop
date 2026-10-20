import { createRoot } from 'react-dom/client'
import './index.css'
import './features/recipient/recipient.css'
import App from './App.tsx'

// T-003-09 dev-only: StrictMode double-mount fires two parallel GETs that race the singleton WaDbContext (pre-existing since T-002-02) → EF concurrency 500. No-op in prod builds; restore once DI is scoped.
createRoot(document.getElementById('root')!).render(<App />)
