#!/usr/bin/env bash
# Seed the Key Vault secrets Bicep cannot generate (TA-9.3). Idempotent: an existing secret is never
# overwritten, so re-running the CD keeps sessions, unlock JWTs and unsubscribe links valid.
#   wa-jwt-secret            64 hex chars (Wa:Jwt:Secret; rotation 90 d is a manual, planned step)
#   wa-zip-sign-key          64 hex chars (Wa:Zip:SignKey)
#   wa-stripe-secret-key     STRIPE_SECRET_KEY env, else a "not-configured" placeholder (D-12 / P-08)
#   wa-stripe-webhook-secret STRIPE_WEBHOOK_SECRET env, else a placeholder
#   wa-ch-connection         prod only, when Communication Services is created by hand (D-13 / P-06):
#                            CH_CONNECTION env; dev/staging get it from Bicep.
# Usage: seed-secrets.sh <key-vault-name>
set -euo pipefail
kv="${1:?key vault name}"

exists() { az keyvault secret show --vault-name "$kv" --name "$1" --query id -o tsv >/dev/null 2>&1; }

seed() {
  local name="$1" value="$2"
  if exists "$name"; then
    echo "keep   $name"
  else
    az keyvault secret set --vault-name "$kv" --name "$name" --value "$value" --output none
    echo "create $name"
  fi
}

# Stripe values may change (test -> live): update them when provided, otherwise only seed the placeholder.
upsert_if_given() {
  local name="$1" value="${2:-}"
  if [[ -n "$value" ]]; then
    az keyvault secret set --vault-name "$kv" --name "$name" --value "$value" --output none
    echo "set    $name"
  else
    seed "$name" "not-configured"
  fi
}

seed wa-jwt-secret "$(openssl rand -hex 32)"
seed wa-zip-sign-key "$(openssl rand -hex 32)"
upsert_if_given wa-stripe-secret-key "${STRIPE_SECRET_KEY:-}"
upsert_if_given wa-stripe-webhook-secret "${STRIPE_WEBHOOK_SECRET:-}"
if [[ -n "${CH_CONNECTION:-}" ]]; then
  upsert_if_given wa-ch-connection "$CH_CONNECTION"
fi
