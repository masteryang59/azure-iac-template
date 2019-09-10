#!/usr/bin/env bash
# usage: what-if.sh <resource-group> <params.bicepparam> <summary.md>
# Runs what-if and writes a Markdown summary for the pipeline run page.

source "$(dirname "$0")/common.sh"

rg="$1"
params="$2"
summary="$3"

require_resource_group "$rg"

az deployment group what-if \
  --resource-group "$rg" \
  --parameters "$params" \
  --exclude-change-types Ignore NoChange \
  --result-format ResourceIdOnly \
  --no-pretty-print \
  --output json > what-if.json

{
  echo "## What-if: \`$rg\`"
  echo
  count=$(jq '.changes | length' what-if.json)
  if [[ "$count" == "0" ]]; then
    echo "No changes."
  else
    echo "| Change | Resource |"
    echo "| --- | --- |"
    jq -r '.changes[] | "| \(.changeType) | `\(.resourceId | split("/providers/") | last)` |"' what-if.json
  fi
  echo
  echo "Ignored and unchanged resources are omitted. The full result is in the job log."
} > "$summary"

cat "$summary"

# Human-readable diff with property-level changes, for the log.
az deployment group what-if \
  --resource-group "$rg" \
  --parameters "$params" \
  --exclude-change-types Ignore NoChange \
  --result-format FullResourcePayloads

aggregate_stats() {
  local input="$1"
  [ -z "$input" ] && return 1
  echo "$input"
}

aggregate_stats() {
  local input="$1"
  [ -z "$input" ] && return 1
  echo "$input"
}