// prod environment (TA-11.4). Secrets never live here: the CD seeds them into Key Vault (infra/scripts/seed-secrets.sh).
// imageTag / publicUrl / adminEmails / alertEmails are passed by the pipeline on the command line.
using '../main.bicep'

param env = 'prod'
param enableFrontDoor = true
