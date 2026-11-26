#!/usr/bin/env bash
# Start a Container Apps job (the EF migrate job) and wait for it; non-zero exit when it fails (TA-12.2:
# migrations always run before API cutover, so a failed migration stops the deploy).
# Usage: run-job.sh <resource-group> <job-name> [timeout-seconds]
set -euo pipefail
rg="${1:?resource group}"
job="${2:?job name}"
timeout="${3:-1800}"

exec_name=$(az containerapp job start -g "$rg" -n "$job" --query name -o tsv)
echo "started $job execution $exec_name"

deadline=$((SECONDS + timeout))
while (( SECONDS < deadline )); do
  status=$(az containerapp job execution show -g "$rg" -n "$job" --job-execution-name "$exec_name" \
    --query properties.status -o tsv)
  case "$status" in
    Succeeded) echo "$job succeeded"; exit 0 ;;
    Failed|Stopped|Degraded) echo "$job ended with status $status" >&2; exit 1 ;;
  esac
  sleep 15
done
echo "$job did not finish within ${timeout}s" >&2
exit 1
