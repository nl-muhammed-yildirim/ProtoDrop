// US-024-03 (TA-8.4): non-blocking, de-duplicated ad script injector. No package; plain DOM.
const loads = new Map<string, Promise<void>>()

export function loadAdScript(src: string): Promise<void> {
  const existing = loads.get(src)
  if (existing) return existing

  const promise = new Promise<void>((resolve, reject) => {
    const el = document.createElement('script')
    el.src = src
    el.defer = true
    el.async = true
    el.crossOrigin = 'anonymous'
    el.dataset.adLoader = 'true'
    el.onload = () => resolve()
    el.onerror = () => {
      el.remove()
      loads.delete(src) // a later mount may retry
      reject(new Error('ad script failed to load'))
    }
    document.head.appendChild(el)
  })
  loads.set(src, promise)
  return promise
}

export function resetAdScriptsForTest(): void {
  loads.clear()
  document.head.querySelectorAll('script[data-ad-loader]').forEach((s) => s.remove())
}
