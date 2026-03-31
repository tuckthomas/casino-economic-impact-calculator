#!/usr/bin/env bash

set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/dev-common.sh"

cleanup_stale_pidfile

if [[ ! -f "${PIDFILE}" ]]; then
  info "Local dev watcher: not running"
  exit 0
fi

assert_pidfile_matches_repo

info "Local dev watcher: running"
info "PID: ${PID}"
info "PGID: ${PGID:-unknown}"
info "Mode: ${MODE:-unknown}"
info "Port: ${PORT}"
info "Log: ${LOGFILE}"
