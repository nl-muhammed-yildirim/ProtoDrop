import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'

import App from './App'

describe('App landing', () => {
  it('renders the drop zone heading and ARIA (F-TRF-001)', () => {
    const html = renderToStaticMarkup(<App />)
    expect(html).toContain('<h1 class="dz-title">Send your files.</h1>')
    expect(html).toContain('aria-label="Upload files"')
  })
})
