/**
 * T-016-02 (FR-016-3): single source for toast ARIA roles. UI-Reference §4.3 — success/info toasts are
 * polite status regions, error toasts are assertive alerts. Every screen that renders a toast maps its kind
 * through this function so the role assignment never drifts per component (no hardcoded role="status"/"alert").
 */

type ToastKind = 'success' | 'error'

export function toastRole(kind: ToastKind): 'status' | 'alert' {
  return kind === 'error' ? 'alert' : 'status'
}
