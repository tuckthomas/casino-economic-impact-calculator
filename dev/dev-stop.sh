#!/usr/bin/env bash

set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/dev-common.sh"

cleanup_stale_pidfile

if [[ ! -f "${PIDFILE}" ]]; then
  info "No recorded local dev watcher is running."
  exit 0
fi

info "Stopping recorded local dev watcher."
stop_recorded_process
info "Local dev watcher stopped."
