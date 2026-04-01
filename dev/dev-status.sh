#!/usr/bin/env bash

set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/dev-common.sh"

cleanup_stale_pidfile

if [[ ! -f "${PIDFILE}" ]]; then
  info "Local dev process: not running"
  exit 0
fi

assert_pidfile_matches_repo

info "Local dev process: running"
info "PID: ${PID}"
info "PGID: ${PGID:-unknown}"
info "Mode: ${MODE:-unknown}"
info "Port: ${PORT}"
info "Log: ${LOGFILE}"
