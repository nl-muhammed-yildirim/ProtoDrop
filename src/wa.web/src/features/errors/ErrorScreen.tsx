import i18n from '../../core/i18n/init'

/**
 * T-013-02 (FR-013-1/2/5, UI-Reference §4.6): The single §4.6 error layout — full page, centered;
 * icon `alert`; one line at --fs-h1 (DECISION: no --fs-h2 in :root); one primary button; optional ref line
 * "Ref: {id8}" in --fs-tiny / --fg-muted. No stack traces or raw codes ever rendered.
 *
 * EC-013-3: all copy is a translation key (T-026 owns the 8-locale pass); the "Ref:" label is localized,
 * the 8-hex id is never a key — it's rendered raw from correlationId.
 */

// TA-8.5 {area}.{screen} pattern — English values verbatim from spec (T-026 will localize).
export const ERROR_COPY = {
  // 500 screen (FR-013-2): "Something went wrong." + Try again + Ref from correlationId.
  somethingWrongTitle: i18n.t('error.title.somethingWrong'),
  tryAgain: i18n.t('landing.staging.tryAgain'),
  // SPA 404 screen (FR-013-5): "Oops — this page moved." + Go to home, NO ref line (no request happened).
  notFoundTitle: i18n.t('error.title.notFound'),
  goHome: i18n.t('error.action.goHome'),
  // EC-013-3: the "Ref:" label is localized; the id is not.
  refLabel: i18n.t('error.ref.label'),
  // RATE_LIMITED (EC-013-3): "A moment of calm, then try again." with the 60 s hint — NO Ref line.
  rateLimitedTitle: i18n.t('error.title.rateLimited'),
  rateLimitedHint: i18n.t('error.hint.rateLimited'),
} as const

export interface ErrorScreenProps {
  /** Translation key for the one-line heading (rendered via ERROR_COPY). */
  titleKey: keyof typeof ERROR_COPY
  /** Translation key for the primary button label. */
  actionLabelKey: keyof typeof ERROR_COPY
  /** The retry/home handler — re-issues the failed action (no full reload unless it was a page load). */
  onAction: () => void
  /** Optional correlationId — renders the "Ref: {id8}" line when present. */
  correlationId?: string
}

/**
 * The §4.6 error layout — the only error screen shape in the app.
 * Renders: alert icon, one line (title), one primary button, optional Ref line.
 */
export function ErrorScreen({ titleKey, actionLabelKey, onAction, correlationId }: ErrorScreenProps) {
  return (
    <section className="es" aria-label={ERROR_COPY[titleKey]}>
      <span className="es-icon es-alert" aria-hidden="true">
        {/* feather alert-triangle — inline, features self-contained (TA-8.2). */}
        <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
          <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z" />
          <line x1="12" y1="9" x2="12" y2="13" />
          <line x1="12" y1="17" x2="12.01" y2="17" />
        </svg>
      </span>
      <h1 className="es-heading">{ERROR_COPY[titleKey]}</h1>
      {/* RATE_LIMITED hint — rendered as a second line when present (EC-013-3: 60 s hint, no Ref). */}
      {titleKey === 'rateLimitedTitle' && <p className="es-hint">{ERROR_COPY.rateLimitedHint}</p>}
      <button type="button" className="btn-primary es-action" onClick={onAction}>
        {ERROR_COPY[actionLabelKey]}
      </button>
      {/* Ref line — only when correlationId is present (500 screen); 404 has no request → no Ref. */}
      {correlationId && (
        <p className="es-ref">
          {ERROR_COPY.refLabel} {correlationId}
        </p>
      )}
    </section>
  )
}

export default ErrorScreen
