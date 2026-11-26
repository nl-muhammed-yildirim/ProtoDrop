import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
// T-015-01 (F-TRF-015): token definitions first — must precede the usage rules in index.css.
import './styles/tokens.css'
import './index.css'
import './features/recipient/recipient.css'
import './features/errors/errorMap.css'
import './core/i18n' // T-014-01: i18next init before first render (synchronous on import)
import App from './App.tsx'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
