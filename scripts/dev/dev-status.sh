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
info "Hot Reload WS Host: ${DOTNET_WATCH_WS_HOSTNAME}"
info "Browser Refresh Suppressed: ${DOTNET_WATCH_BROWSER_REFRESH_SUPPRESSED}"
if [[ "${DOTNET_WATCH_BROWSER_REFRESH_SUPPRESSED}" == "1" ]]; then
  info "Hot Reload Enabled: 0"
else
  info "Hot Reload Enabled: 1"
fi
info "Log: ${LOGFILE}"
