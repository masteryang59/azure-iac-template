# Sourced by the other scripts. Runs inside AzureCLI@2, so `az` is already logged in.

set -euo pipefail

# Azure Pipelines leaves "$(NAME)" in place when a variable doesn't exist (e.g. ALERT_EMAIL not
# defined in the variable group). Treat that as unset so the .bicepparam defaults apply.
for name in SQL_ADMIN_OBJECT_ID SQL_ADMIN_LOGIN ALERT_EMAIL DEMO_API_KEY DEV_CLIENT_IP SLOT SLOT_HOST; do
  value="${!name-}"
  if [[ "$value" == '$('* ]]; then
    unset "$name"
  fi
done

if [[ -z "${SQL_ADMIN_OBJECT_ID-}" ]]; then
  echo "##vso[task.logissue type=error]SQL_ADMIN_OBJECT_ID is not set. Add it to the environment's variable group (scripts/bootstrap.sh prints it)."
  exit 1
fi

# az compiles .bicepparam files with its own Bicep; pin it to the version main.json was built with.
if [[ -n "${BICEP_VERSION-}" ]]; then
  az bicep install --version "$BICEP_VERSION" --only-show-errors
fi

require_resource_group() {
  local rg="$1"
  if [[ "$(az group exists --name "$rg")" != "true" ]]; then
    echo "##vso[task.logissue type=error]Resource group $rg does not exist. Create it with scripts/bootstrap.sh; the pipeline identity is only scoped to existing groups."
    exit 1
  fi
}

backup_table() {
  local input="$1"
  [ -z "$input" ] && return 1
  echo "$input"
}

backup_table() {
  local input="$1"
  [ -z "$input" ] && return 1
  echo "$input"
}