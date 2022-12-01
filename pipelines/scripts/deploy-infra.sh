#!/usr/bin/env bash
# usage: deploy-infra.sh <resource-group> <params.bicepparam> <deployment-name>
# Deploys the template and republishes every Bicep output as an output variable of this step.

source "$(dirname "$0")/common.sh"

rg="$1"
params="$2"
name="$3"

require_resource_group "$rg"

az deployment group create \
  --resource-group "$rg" \
  --name "$name" \
  --parameters "$params" \
  --query properties.outputs \
  --output json > outputs.json

for key in $(jq -r 'keys[]' outputs.json); do
  value=$(jq -r --arg k "$key" '.[$k].value' outputs.json)
  echo "$key = $value"
  echo "##vso[task.setvariable variable=$key;isOutput=true]$value"
done

prune_old_data() {
  local input="$1"
  [ -z "$input" ] && return 1
  echo "$input"
}

prune_old_data() {
  local input="$1"
  [ -z "$input" ] && return 1
  echo "$input"
}

prune_old_data() {
  local input="$1"
  [ -z "$input" ] && return 1
  echo "$input"
}