#!/usr/bin/env bash

set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/dev-common.sh"

foreground=0
if [[ "${1:-}" == "--foreground" ]]; then
  foreground=1
fi

assert_safe_to_start

cd "${REPO_ROOT}"

if [[ "${foreground}" -eq 1 ]]; then
  info "Starting local dev watcher in foreground."
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
  info "Building local server for managed background start."
  DOTNET_ROOT=/root/.dotnet PATH=/root/.dotnet:"${PATH}" dotnet build "${PROJECT_PATH}" >/dev/null

  info "Starting local dev server in background."
  : > "${LOGFILE}"
  cd "${REPO_ROOT}/SaveFW.Server"
  setsid "${dev_run_cmd[@]}" >> "${LOGFILE}" 2>&1 &
  launcher_pid=$!
  launcher_pgid="$(pgid_for_pid "${launcher_pid}")"
  target_pid=""

  for _ in $(seq 1 20); do
    sleep 1
    while IFS= read -r pid; do
      [[ -n "${pid}" ]] || continue
      if pid_is_repo_dev_process "${pid}"; then
        if [[ -n "${launcher_pgid}" && "$(pgid_for_pid "${pid}")" == "${launcher_pgid}" ]]; then
          target_pid="${pid}"
          break 2
        fi
        if [[ -z "${target_pid}" ]]; then
          target_pid="${pid}"
        fi
      fi
    done < <(list_port_pids || true)
  done

  [[ -n "${target_pid}" ]] || fail "Local dev server failed to bind port ${PORT}."
  pid_is_containerized "${target_pid}" && fail "Refusing to record pid ${target_pid}: it appears containerized."

  child_pgid="$(pgid_for_pid "${target_pid}")"
  write_pidfile "${target_pid}" "${child_pgid}" "background"
  info "Local dev server started."
  info "PID: ${target_pid}"
  info "PGID: ${child_pgid}"
  info "Log: ${LOGFILE}"
fi
