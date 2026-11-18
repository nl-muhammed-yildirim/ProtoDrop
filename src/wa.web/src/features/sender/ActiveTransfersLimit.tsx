import i18n from '../../core/i18n/init'
import { toastRole } from '../../core/ui/toastRole'
import { parseActiveTransfersLimit } from '../../core/api/errorMap'
import { PLAN_PATH } from '../billing/planRoute'

/**
 * US-020-02 (FR-020-6, AC-020-2): the ACTIVE_TRANSFERS_EXCEEDED paywall — exact number + Upgrade CTA to /account/plan.
 * Shared by the finalize (StagingList) and send (LinkScreen) failure paths; `details` is the Problem+JSON details text.
 */
export function ActiveTransfersLimit({ details }: { details?: string }) {
  const nums = parseActiveTransfersLimit(details)
  const message = nums
    ? i18n.t('limit.activeTransfers.exact', { count: nums.count, limit: nums.limit })
    : i18n.t('limit.activeTransfers.generic')
  return (
    <div className="atl">
      <p role={toastRole('error')} className="atl-msg">{message}</p>
      <a className="atl-upgrade" href={PLAN_PATH}>{i18n.t('billing.plan.upgrade')}</a>
    </div>
  )
}

export default ActiveTransfersLimit
