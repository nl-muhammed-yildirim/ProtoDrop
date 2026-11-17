/**
 * T-013-02 (FR-013-2, TA-10.5 precedent): React class ErrorBoundary wrapping the app.
 * On a client-side render error it renders the 500 screen via ErrorScreen with
 * Try again = window.location.reload() — the failing action WAS a page load, so reload is correct.
 * The caught error is logged to console.error (stack stays in logs, out of UI).
 */

import { Component, type ReactNode } from 'react'
import ErrorScreen from './ErrorScreen'

interface AppErrorBoundaryProps {
  children: ReactNode
}

interface AppErrorBoundaryState {
  hasError: boolean
}

export class AppErrorBoundary extends Component<AppErrorBoundaryProps, AppErrorBoundaryState> {
  state: AppErrorBoundaryState = { hasError: false }

  static getDerivedStateFromError(): AppErrorBoundaryState {
    return { hasError: true }
  }

  componentDidCatch(error: Error, errorInfo: React.ErrorInfo): void {
    // Stack traces stay in logs, out of UI (TA-10.5 precedent).
    console.error('AppErrorBoundary caught:', error, errorInfo.componentStack)
  }

  render(): ReactNode {
    if (this.state.hasError) {
      return (
        <ErrorScreen
          titleKey="somethingWrongTitle"
          actionLabelKey="tryAgain"
          onAction={() => window.location.reload()}
        />
      )
    }
    return this.props.children
  }
}

export default AppErrorBoundary
