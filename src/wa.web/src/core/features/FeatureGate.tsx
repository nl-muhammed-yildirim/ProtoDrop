import { useEffect } from 'react'
import type { ReactNode } from 'react'

import { loadFeatureGates, useFeatureGate } from './featureGates'
import type { FeatureGateKey } from './featureGates'

interface FeatureGateProps {
  gate: FeatureGateKey
  children: ReactNode
}

/**
 * US-044-02 (AC-044-2): renders its children only while the phase gate is on — nothing at all otherwise
 * (absent, never disabled). Mounting kicks the single shared gate read; the API filter stays the authority.
 */
export function FeatureGate({ gate, children }: FeatureGateProps) {
  const on = useFeatureGate(gate)
  useEffect(() => {
    void loadFeatureGates()
  }, [])
  return on ? <>{children}</> : null
}

export default FeatureGate
