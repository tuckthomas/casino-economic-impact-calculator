#!/usr/bin/env bash

set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/dev-common.sh"

foreground=0
if [[ "${1:-}" == "--foreground" ]]; then
  foreground=1
fi

assert_safe_to_start
ensure_watch_home

cd "${REPO_ROOT}"

if [[ "${foreground}" -eq 1 ]]; then
  info "Starting local dev watcher in foreground."
  mapfile -d '' -t dev_watch_cmd < <(build_watch_cmd foreground)
  "${dev_watch_cmd[@]}" &
  child_pid=$!
  child_pgid="$(pgid_for_pid "${child_pid}")"
  write_pidfile "${child_pid}" "${child_pgid}" "foreground"

  cleanup() {
    remove_pidfile
  }

  trap cleanup EXIT INT TERM
  wait "${child_pid}"
else
  info "Starting local dev watcher in background."
  : > "${LOGFILE}"
  cd "${REPO_ROOT}"
  mapfile -d '' -t dev_watch_cmd < <(build_watch_cmd background)
  setsid "${dev_watch_cmd[@]}" >> "${LOGFILE}" 2>&1 &
  watcher_pid=$!
  watcher_pgid="$(pgid_for_pid "${watcher_pid}")"

  for _ in $(seq 1 60); do
    sleep 1
    if ! process_exists "${watcher_pid}"; then
      fail "Local dev watcher exited before binding port ${PORT}. See ${LOGFILE}."
    fi

    if ss -ltn "( sport = :${PORT} )" 2>/dev/null | grep -q ":${PORT}"; then
      break
    fi
  done

  if ! ss -ltn "( sport = :${PORT} )" 2>/dev/null | grep -q ":${PORT}"; then
    fail "Local dev watcher failed to bind port ${PORT}. See ${LOGFILE}."
  fi

  pid_is_containerized "${watcher_pid}" && fail "Refusing to record pid ${watcher_pid}: it appears containerized."

  write_pidfile "${watcher_pid}" "${watcher_pgid}" "background"
  info "Local dev watcher started."
  info "PID: ${watcher_pid}"
  info "PGID: ${watcher_pgid}"
  info "Log: ${LOGFILE}"
fi
