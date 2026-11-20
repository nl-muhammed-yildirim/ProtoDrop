// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { AdSlot } from './AdSlot'
import type { AdLoader, AdLoadResult } from './adSlotState'
import { AD_LOAD_TIMEOUT_MS } from './useAdSlot'

interface Deferred {
  load: AdLoader
  spy: ReturnType<typeof vi.fn>
  resolve: (r: AdLoadResult) => void
  reject: (e: unknown) => void
}

function deferred(): Deferred {
  let resolve: (r: AdLoadResult) => void = () => {}
  let reject: (e: unknown) => void = () => {}
  const spy = vi.fn(
    () =>
      new Promise<AdLoadResult>((res, rej) => {
        resolve = res
        reject = rej
      }),
  )
  return { load: spy as unknown as AdLoader, spy, resolve: (r) => resolve(r), reject: (e) => reject(e) }
}

let errorSpy: ReturnType<typeof vi.spyOn>

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'requestAnimationFrame', 'cancelAnimationFrame'] })
  errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {})
})

afterEach(() => {
  cleanup()
  vi.useRealTimers()
  errorSpy.mockRestore()
})

const slot = (c: HTMLElement) => c.querySelector('.rp-ad') as HTMLElement
const frame = () =>
  act(async () => {
    vi.advanceTimersByTime(16)
  })

function expectSilent(c: HTMLElement) {
  expect(c.textContent).toBe('')
  expect(screen.queryByRole('alert')).toBeNull()
  expect(screen.queryByRole('status')).toBeNull()
  expect(errorSpy).not.toHaveBeenCalled()
}

describe('AdSlot', () => {
  it('TA-8.4: idle until first frame, then loading with the unit element', async () => {
    const d = deferred()
    const { container } = render(<AdSlot slotId="s1" load={d.load} />)
    expect(slot(container).dataset.state).toBe('idle')
    expect(d.spy).not.toHaveBeenCalled()
    await frame()
    expect(slot(container).dataset.state).toBe('loading')
    expect(d.spy).toHaveBeenCalledTimes(1)
    expect(d.spy.mock.calls[0][0]).toBe(container.querySelector('.rp-ad-unit'))
  })

  it('filled: one impression outcome, visible to AT', async () => {
    const d = deferred()
    const onOutcome = vi.fn()
    const { container } = render(<AdSlot slotId="s1" load={d.load} onOutcome={onOutcome} />)
    await frame()
    await act(async () => {
      d.resolve('filled')
    })
    expect(slot(container).dataset.state).toBe('filled')
    expect(slot(container).hasAttribute('aria-hidden')).toBe(false)
    expect(onOutcome).toHaveBeenCalledTimes(1)
    expect(onOutcome).toHaveBeenCalledWith({ slotId: 's1', kind: 'impression' })
  })

  it('FR-024-2: empty response collapses silently', async () => {
    const d = deferred()
    const onOutcome = vi.fn()
    const { container } = render(<AdSlot slotId="s1" load={d.load} onOutcome={onOutcome} />)
    await frame()
    await act(async () => {
      d.resolve('empty')
    })
    expect(slot(container).dataset.state).toBe('collapsed')
    expect(onOutcome).toHaveBeenCalledWith({ slotId: 's1', kind: 'error', reason: 'empty' })
    expectSilent(container)
  })

  it('EC-024-5: rejected loader collapses with script_error', async () => {
    const d = deferred()
    const onOutcome = vi.fn()
    const { container } = render(<AdSlot slotId="s1" load={d.load} onOutcome={onOutcome} />)
    await frame()
    await act(async () => {
      d.reject(new Error('blocked'))
    })
    expect(slot(container).dataset.state).toBe('collapsed')
    expect(onOutcome).toHaveBeenCalledWith({ slotId: 's1', kind: 'error', reason: 'script_error' })
    expectSilent(container)
  })

  it('EC-024-5: synchronously throwing loader collapses too', async () => {
    const onOutcome = vi.fn()
    const load: AdLoader = () => {
      throw new Error('boom')
    }
    const { container } = render(<AdSlot slotId="s1" load={load} onOutcome={onOutcome} />)
    await frame()
    await act(async () => {})
    expect(slot(container).dataset.state).toBe('collapsed')
    expect(onOutcome).toHaveBeenCalledWith({ slotId: 's1', kind: 'error', reason: 'script_error' })
    expectSilent(container)
  })

  it('AC-024-3: 60 s timeout collapses; a late fill is ignored', async () => {
    const d = deferred()
    const onOutcome = vi.fn()
    const { container } = render(<AdSlot slotId="s1" load={d.load} onOutcome={onOutcome} />)
    await frame()
    act(() => {
      vi.advanceTimersByTime(AD_LOAD_TIMEOUT_MS - 1)
    })
    expect(slot(container).dataset.state).toBe('loading')
    act(() => {
      vi.advanceTimersByTime(1)
    })
    expect(slot(container).dataset.state).toBe('collapsed')
    expect(onOutcome).toHaveBeenCalledWith({ slotId: 's1', kind: 'error', reason: 'timeout' })
    await act(async () => {
      d.resolve('filled')
    })
    expect(slot(container).dataset.state).toBe('collapsed')
    expect(onOutcome).toHaveBeenCalledTimes(1)
    expectSilent(container)
  })

  it('EC-024-2: page buttons stay clickable while loading and after collapse', async () => {
    const d = deferred()
    const onDownload = vi.fn()
    const { container } = render(
      <div>
        <button onClick={onDownload} aria-label="dl" />
        <AdSlot slotId="s1" load={d.load} />
      </div>,
    )
    await frame()
    expect(slot(container).dataset.state).toBe('loading')
    fireEvent.click(screen.getByRole('button'))
    act(() => {
      vi.advanceTimersByTime(AD_LOAD_TIMEOUT_MS)
    })
    expect(slot(container).dataset.state).toBe('collapsed')
    fireEvent.click(screen.getByRole('button'))
    expect(onDownload).toHaveBeenCalledTimes(2)
  })

  it('unmount mid-load: late settle and timeout do nothing', async () => {
    const d = deferred()
    const onOutcome = vi.fn()
    const { unmount } = render(<AdSlot slotId="s1" load={d.load} onOutcome={onOutcome} />)
    await frame()
    unmount()
    await act(async () => {
      d.resolve('filled')
    })
    act(() => {
      vi.advanceTimersByTime(AD_LOAD_TIMEOUT_MS + 1)
    })
    expect(onOutcome).not.toHaveBeenCalled()
    expect(errorSpy).not.toHaveBeenCalled()
  })

  it('works without onOutcome', async () => {
    const d = deferred()
    const { container } = render(<AdSlot slotId="s1" load={d.load} />)
    await frame()
    await act(async () => {
      d.resolve('empty')
    })
    expect(slot(container).dataset.state).toBe('collapsed')
  })
  it('US-024-01 label: shown only while filled; loading stays text-free', async () => {
    const d = deferred()
    const { container } = render(<AdSlot slotId="s1" load={d.load} label="Ad" />)
    await frame()
    expect(container.querySelector('.rp-ad-label')).toBeNull()
    expect(container.textContent).toBe('')
    await act(async () => {
      d.resolve('filled')
    })
    const labels = container.querySelectorAll('.rp-ad-label')
    expect(labels).toHaveLength(1)
    expect(labels[0].textContent).toBe('Ad')
    expect(container.querySelector('.rp-ad-unit')!.contains(labels[0])).toBe(false)
  })

  it('US-024-01 label: collapsed slot with a label stays silent', async () => {
    const d = deferred()
    const { container } = render(<AdSlot slotId="s1" load={d.load} label="Ad" />)
    await frame()
    await act(async () => {
      d.resolve('empty')
    })
    expect(slot(container).dataset.state).toBe('collapsed')
    expect(container.querySelector('.rp-ad-label')).toBeNull()
    expectSilent(container)
  })

  it('US-024-01 label: no label prop -> filled slot stays text-free', async () => {
    const d = deferred()
    const { container } = render(<AdSlot slotId="s1" load={d.load} />)
    await frame()
    await act(async () => {
      d.resolve('filled')
    })
    expect(slot(container).dataset.state).toBe('filled')
    expectSilent(container)
  })
})
