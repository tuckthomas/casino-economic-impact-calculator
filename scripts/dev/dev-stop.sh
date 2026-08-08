#!/usr/bin/env bash

set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/dev-common.sh"

cleanup_stale_pidfile

if [[ ! -f "${PIDFILE}" ]]; then
  info "No recorded local dev process is running."
  exit 0
fi

info "Stopping recorded local dev process."
stop_recorded_process
info "Local dev process stopped."
