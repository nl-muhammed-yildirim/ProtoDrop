// US-024-01 (D3, FR-024-5): Google AdSense provider behind the AdLoader port. No package (TA-17); plain DOM.
import type { AdConfig } from './adConfig'
import type { AdLoader, AdLoadResult } from './adSlotState'
import { loadAdScript } from './loadAdScript'

const SCRIPT_BASE = 'https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js'

function statusToResult(status: string | null): AdLoadResult | null {
  if (status === 'filled') return 'filled'
  if (status === 'unfilled') return 'empty'
  return null
}

export function createAdSenseLoader(config: AdConfig): AdLoader {
  return async (container, signal) => {
    await loadAdScript(`${SCRIPT_BASE}?client=${encodeURIComponent(config.client)}`)

    const ins = document.createElement('ins')
    ins.className = 'adsbygoogle'
    ins.style.display = 'block'
    ins.style.width = '100%'
    ins.style.height = '100%'
    ins.setAttribute('data-ad-client', config.client)
    ins.setAttribute('data-ad-slot', config.unit)
    // FR-024-5: the provider is told the site origin, never /t/{linkId} (E2 decided: origin only).
    ins.setAttribute('data-page-url', `${window.location.origin}/`)
    container.appendChild(ins)

    const w = window as unknown as { adsbygoogle?: unknown[] }
    const queue = (w.adsbygoogle = w.adsbygoogle ?? [])
    queue.push({})

    return new Promise<AdLoadResult>((resolve, reject) => {
      const now = statusToResult(ins.getAttribute('data-ad-status'))
      if (now) {
        resolve(now)
        return
      }
      if (signal?.aborted) {
        reject(new DOMException('aborted', 'AbortError'))
        return
      }
      const onAbort = () => {
        observer.disconnect()
        reject(new DOMException('aborted', 'AbortError'))
      }
      const observer = new MutationObserver(() => {
        const result = statusToResult(ins.getAttribute('data-ad-status'))
        if (result) {
          observer.disconnect()
          signal?.removeEventListener('abort', onAbort)
          resolve(result)
        }
      })
      observer.observe(ins, { attributes: true, attributeFilter: ['data-ad-status'] })
      signal?.addEventListener('abort', onAbort, { once: true })
    })
  }
}
