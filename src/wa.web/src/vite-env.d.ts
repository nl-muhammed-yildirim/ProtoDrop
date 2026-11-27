/** CHORE-deploy-hardening: typed build-time env (Vite inlines `VITE_*`). */
interface ImportMetaEnv {
  /** API base URL (build ARG); blank/missing means the local default. */
  readonly VITE_WA_API_BASE?: string
}
