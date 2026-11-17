// @vitest-environment jsdom
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'

import ErrorScreen from './ErrorScreen'

describe('ErrorScreen — 500 instantiation (FR-013-2)', () => {
  it('renders title + Try again + Ref line with raw 8-hex correlationId', () => {
    const html = renderToStaticMarkup(
      <ErrorScreen
        titleKey="somethingWrongTitle"
        actionLabelKey="tryAgain"
        onAction={() => {}}
        correlationId="a1b2c3d4"
      />
    )
    expect(html).toContain('Something went wrong.')
    expect(html).toContain('Try again')
    expect(html).toContain('Ref: a1b2c3d4')
  })
})

describe('ErrorScreen — SPA 404 instantiation (FR-013-5)', () => {
  it('renders copy + Go to home and NO ref line', () => {
    const html = renderToStaticMarkup(
      <ErrorScreen
        titleKey="notFoundTitle"
        actionLabelKey="goHome"
        onAction={() => {}}
      />
    )
    expect(html).toContain('Oops — this page moved.')
    expect(html).toContain('Go to home')
    // No correlationId → no Ref line.
    expect(html).not.toContain('Ref:')
  })
})
