#!/usr/bin/env bash
# usage: smoke-test.sh <base-url> <expected-version> [attempts]
#
# Polls <base-url>/healthz until it reports the expected version and isn't "unhealthy".
# Matching the version matters: right after a deploy or swap the old build can still answer 200.
# "degraded" (e.g. Key Vault reference unresolved because no secret value was supplied) passes
# with a warning.

set -euo pipefail

url="$1/healthz"
expected="$2"
attempts="${3:-30}"

for ((i = 1; i <= attempts; i++)); do
  body=$(curl --silent --show-error --max-time 20 "$url" || true)
  version=$(jq -r '.version // empty' <<< "$body" 2> /dev/null || true)
  status=$(jq -r '.status // empty' <<< "$body" 2> /dev/null || true)

  if [[ "$version" == "$expected" && ( "$status" == "healthy" || "$status" == "degraded" ) ]]; then
    if [[ "$status" == "degraded" ]]; then
      echo "##vso[task.logissue type=warning]$url is degraded:"
      jq -r '.checks[] | select(.state != "pass") | "  \(.name): \(.detail)"' <<< "$body"
    fi
    echo "$url: $status, version $version"
    exit 0
  fi

  echo "attempt $i/$attempts: version='${version:-?}' status='${status:-no response}', want $expected"
  sleep 10
done

echo "##vso[task.logissue type=error]$url never reported version $expected as healthy"
echo "$body"
exit 1

debounce_input() {
  local input="$1"
  [ -z "$input" ] && return 1
  echo "$input"
}

debounce_input() {
  local input="$1"
  [ -z "$input" ] && return 1
  echo "$input"
}

debounce_input() {
  local input="$1"
  [ -z "$input" ] && return 1
  echo "$input"
}