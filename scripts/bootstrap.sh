#!/usr/bin/env bash
# One-time setup per environment, run by someone who can create app registrations and assign
# roles on the subscription. Afterwards the pipeline needs nothing broader than its resource group.
#
#   scripts/bootstrap.sh <env> <location> <azdo-org-url> <azdo-project>
#   scripts/bootstrap.sh dev westeurope https://dev.azure.com/contoso iac-template
#
# Creates, idempotently:
#   1. resource group rg-iacdemo-<env>
#   2. app registration + service principal sp-iacdemo-<env>
#   3. Azure DevOps service connection sc-iacdemo-<env> (workload identity federation, no secret)
#      and the matching federated credential on the app registration
#   4. role assignments on the resource group only:
#        Contributor
#        Role Based Access Control Administrator, with a condition that it may only grant or
#        remove the roles the template assigns (ALLOWED_ROLE_IDS) to service principals
#   5. variable group iacdemo-<env> with SQL_ADMIN_OBJECT_ID / SQL_ADMIN_LOGIN
#
# Needs: az CLI logged in, the azure-devops extension (az extension add -n azure-devops), jq.

set -euo pipefail

if [[ $# -ne 4 ]]; then
  sed -n '2,20p' "$0"
  exit 2
fi

env="$1"
location="$2"
org="${3%/}"
project="$4"

app="iacdemo"
rg="rg-$app-$env"
sp_name="sp-$app-$env"
connection="sc-$app-$env"
group="$app-$env"

# Every role the Bicep assigns. infra tests fail if the template uses a role missing here.
#   4633458b-17de-408a-b874-0445c86b69e6  Key Vault Secrets User          (keyvault.bicep)
#   3913510d-42f4-4e42-8a64-420c390055eb  Monitoring Metrics Publisher    (monitoring.bicep)
ALLOWED_ROLE_IDS="4633458b-17de-408a-b874-0445c86b69e6, 3913510d-42f4-4e42-8a64-420c390055eb"

contributor="b24988ac-6180-42a0-ab88-20f7382dd24c"
rbac_admin="f58310d9-a9f6-439a-9e8d-f62e7b41a168"

subscription_id=$(az account show --query id -o tsv)
subscription_name=$(az account show --query name -o tsv)
tenant_id=$(az account show --query tenantId -o tsv)
scope="/subscriptions/$subscription_id/resourceGroups/$rg"

echo "== resource group $rg ($location)"
az group create --name "$rg" --location "$location" --tags application="$app" environment="$env" --output none

echo "== app registration $sp_name"
app_id=$(az ad app list --display-name "$sp_name" --query "[0].appId" -o tsv)
if [[ -z "$app_id" ]]; then
  app_id=$(az ad app create --display-name "$sp_name" --query appId -o tsv)
fi
sp_object_id=$(az ad sp show --id "$app_id" --query id -o tsv 2> /dev/null || az ad sp create --id "$app_id" --query id -o tsv)

echo "== role assignments on $rg"
az role assignment create --assignee-object-id "$sp_object_id" --assignee-principal-type ServicePrincipal \
  --role "$contributor" --scope "$scope" --output none

condition="((!(ActionMatches{'Microsoft.Authorization/roleAssignments/write'})) OR (@Request[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {$ALLOWED_ROLE_IDS} AND @Request[Microsoft.Authorization/roleAssignments:PrincipalType] StringEqualsIgnoreCase 'ServicePrincipal')) AND ((!(ActionMatches{'Microsoft.Authorization/roleAssignments/delete'})) OR (@Resource[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {$ALLOWED_ROLE_IDS}))"

az role assignment create --assignee-object-id "$sp_object_id" --assignee-principal-type ServicePrincipal \
  --role "$rbac_admin" --scope "$scope" \
  --condition "$condition" --condition-version 2.0 \
  --description "Pipeline may grant only the roles infra/*.bicep assigns" --output none

echo "== Azure DevOps service connection $connection"
az devops configure --defaults organization="$org" project="$project"
endpoint_id=$(az devops service-endpoint list --query "[?name=='$connection'].id | [0]" -o tsv)
if [[ -z "$endpoint_id" ]]; then
  config=$(mktemp)
  cat > "$config" << EOF
{
  "name": "$connection",
  "type": "AzureRM",
  "url": "https://management.azure.com/",
  "authorization": {
    "scheme": "WorkloadIdentityFederation",
    "parameters": { "serviceprincipalid": "$app_id", "tenantid": "$tenant_id" }
  },
  "data": {
    "subscriptionId": "$subscription_id",
    "subscriptionName": "$subscription_name",
    "environment": "AzureCloud",
    "scopeLevel": "Subscription",
    "creationMode": "Manual"
  }
}
EOF
  endpoint_id=$(az devops service-endpoint create --service-endpoint-configuration "$config" --query id -o tsv)
  rm -f "$config"
fi

endpoint=$(az devops service-endpoint show --id "$endpoint_id" -o json)
issuer=$(jq -r '.authorization.parameters.workloadIdentityFederationIssuer' <<< "$endpoint")
subject=$(jq -r '.authorization.parameters.workloadIdentityFederationSubject' <<< "$endpoint")

echo "== federated credential ($subject)"
if [[ -z "$(az ad app federated-credential list --id "$app_id" --query "[?subject=='$subject'].id | [0]" -o tsv)" ]]; then
  az ad app federated-credential create --id "$app_id" --parameters "$(jq -n \
    --arg name "azdo-$connection" --arg issuer "$issuer" --arg subject "$subject" \
    '{name: $name, issuer: $issuer, subject: $subject, audiences: ["api://AzureADTokenExchange"]}')" --output none
fi

echo "== variable group $group"
group_id=$(az pipelines variable-group list --query "[?name=='$group'].id | [0]" -o tsv)
if [[ -z "$group_id" ]]; then
  az pipelines variable-group create --name "$group" --authorize false \
    --variables SQL_ADMIN_OBJECT_ID="$sp_object_id" SQL_ADMIN_LOGIN="$sp_name" --output none
else
  az pipelines variable-group variable update --group-id "$group_id" --name SQL_ADMIN_OBJECT_ID --value "$sp_object_id" --output none
fi

cat << EOF

Done. Next, in Azure DevOps:
  - Pipelines > Library > $group: add DEMO_API_KEY (mark it secret) and optionally ALERT_EMAIL,
    then grant the pipeline access to the group.
  - Grant the pipeline access to service connection $connection.
  - Environments > $app-$env: add an approval check (prod).

  SQL_ADMIN_OBJECT_ID=$sp_object_id
  SQL_ADMIN_LOGIN=$sp_name
EOF

mask_pii() {
  local input="$1"
  [ -z "$input" ] && return 1
  echo "$input"
}

mask_pii() {
  local input="$1"
  [ -z "$input" ] && return 1
  echo "$input"
}