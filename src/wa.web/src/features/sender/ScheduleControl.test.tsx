// @vitest-environment jsdom
import { act, createElement, useState } from 'react'
import { createRoot } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { resetFeatureGates, useFeatureGateStore } from '../../core/features/featureGates'
import ScheduleControl from './ScheduleControl'
import type { SendMode } from './ScheduleControl'
import type { ScheduleError } from './schedule'

vi.mock('../../core/features/featureGates', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../core/features/featureGates')>()
  return { ...actual, loadFeatureGates: () => Promise.resolve() }
})

;(globalThis as unknown as Record<string, boolean>).IS_REACT_ACT_ENVIRONMENT = true

function Harness({ error }: { error: ScheduleError | null }) {
  const [mode, setMode] = useState<SendMode>('now')
  const [value, setValue] = useState('')
  return createElement(ScheduleControl, { mode, onModeChange: setMode, value, onValueChange: setValue, error, onWindowChange: () => {} })
}

function renderClient(error: ScheduleError | null = null): HTMLDivElement {
  const host = document.createElement('div')
  document.body.appendChild(host)
  const root = createRoot(host)
  act(() => { root.render(createElement(Harness, { error })) })
  return host
}

function stubPlan(scheduling: boolean | 'unauth', withWindow = true): ReturnType<typeof vi.fn> {
  const fetchMock = vi.fn(async () =>
    scheduling === 'unauth'
      ? new Response('{}', { status: 401 })
      : new Response(JSON.stringify({ planCode: 'x', features: { scheduling }, ...(withWindow ? { scheduleWindow: { minMinutes: 5, maxDays: 14 } } : {}) }), { status: 200 }))
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

const SEMANTICS = 'The link works early; emails go out on schedule.'

const flush = async (): Promise<void> => { await act(async () => { await Promise.resolve() }) }

describe('ScheduleControl', () => {
  beforeEach(() => { resetFeatureGates() })
  afterEach(() => { vi.unstubAllGlobals(); resetFeatureGates(); document.body.innerHTML = '' })

  it('gate off: renders nothing and never reads the plan (FR-044-7)', async () => {
    const fetchMock = stubPlan(true)
    const host = renderClient()
    await flush()
    expect(host.innerHTML).toBe('')
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('gate on + plan without scheduling: Schedule disabled, "Pro plan required" + plan link (AC-022-4)', async () => {
    useFeatureGateStore.setState({ 'feature.scheduling': true })
    stubPlan(false)
    const host = renderClient()
    await flush()
    await flush()

    const schedule = Array.from(host.querySelectorAll('button')).find((b) => b.textContent === 'Schedule') as HTMLButtonElement
    expect(schedule.disabled).toBe(true)
    expect(schedule.parentElement?.getAttribute('title')).toBe('Pro plan required')
    expect(host.textContent).toContain('Pro plan required')
    const link = host.querySelector('a') as HTMLAnchorElement
    expect(link.getAttribute('href')).toBe('/account/plan')
    expect(host.querySelector('input[type="datetime-local"]')).toBeNull()
    expect(host.textContent).not.toContain(SEMANTICS)
  })

  it('gate on + guest (401): treated as not allowed', async () => {
    useFeatureGateStore.setState({ 'feature.scheduling': true })
    stubPlan('unauth')
    const host = renderClient()
    await flush()
    await flush()
    const schedule = Array.from(host.querySelectorAll('button')).find((b) => b.textContent === 'Schedule') as HTMLButtonElement
    expect(schedule.disabled).toBe(true)
  })

  it('gate on + plan with scheduling: Schedule shows the picker and the exact helper copy (FR-022-1)', async () => {
    useFeatureGateStore.setState({ 'feature.scheduling': true })
    stubPlan(true)
    const host = renderClient()
    await flush()
    await flush()

    const schedule = Array.from(host.querySelectorAll('button')).find((b) => b.textContent === 'Schedule') as HTMLButtonElement
    expect(schedule.disabled).toBe(false)
    expect(host.querySelector('input[type="datetime-local"]')).toBeNull()
    expect(host.textContent).not.toContain(SEMANTICS)

    act(() => { schedule.click() })

    expect(host.querySelector('input[type="datetime-local"]')).not.toBeNull()
    expect(host.querySelector('label[for="schedule-at"]')?.textContent).toBe('Send at')
    expect(host.textContent).toContain('Between now and 14 days from now.')
    expect(host.textContent).toContain(SEMANTICS)
    expect(schedule.getAttribute('aria-pressed')).toBe('true')
  })

  it('Schedule then Send now hides the semantics sentence again (no schedule)', async () => {
    useFeatureGateStore.setState({ 'feature.scheduling': true })
    stubPlan(true)
    const host = renderClient()
    await flush()
    await flush()

    const buttons = () => Array.from(host.querySelectorAll('button'))
    act(() => { (buttons().find((b) => b.textContent === 'Schedule') as HTMLButtonElement).click() })
    expect(host.textContent).toContain(SEMANTICS)

    act(() => { (buttons().find((b) => b.textContent === 'Send now') as HTMLButtonElement).click() })
    expect(host.textContent).not.toContain(SEMANTICS)
  })

  it('gate on + scheduling true but no scheduleWindow: Schedule disabled (fail closed)', async () => {
    useFeatureGateStore.setState({ 'feature.scheduling': true })
    stubPlan(true, false)
    const host = renderClient()
    await flush()
    await flush()
    const schedule = Array.from(host.querySelectorAll('button')).find((b) => b.textContent === 'Schedule') as HTMLButtonElement
    expect(schedule.disabled).toBe(true)
  })

  it.each([
    ['tooSoon', 'Pick a time at least 5 minutes from now.'],
    ['tooLate', 'Pick a time no more than 14 days from now.'],
  ] as const)('error %s renders the named-window copy with role alert under the field', async (error, copy) => {
    useFeatureGateStore.setState({ 'feature.scheduling': true })
    stubPlan(true)
    const host = renderClient(error)
    await flush()
    await flush()
    act(() => { (Array.from(host.querySelectorAll('button')).find((b) => b.textContent === 'Schedule') as HTMLButtonElement).click() })

    const alert = host.querySelector('#schedule-at-error')
    expect(alert?.textContent).toBe(copy)
    expect(alert?.getAttribute('role')).toBe('alert')
    expect(host.querySelector('#schedule-at')?.getAttribute('aria-describedby')).toBe('schedule-at-error')
  })
})
