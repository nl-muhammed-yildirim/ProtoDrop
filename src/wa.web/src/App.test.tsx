import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'

import App from './App'

describe('App landing stub', () => {
  it('renders the ProtoDrop heading', () => {
    const html = renderToStaticMarkup(<App />)
    expect(html).toContain('<h1>ProtoDrop</h1>')
  })
})
