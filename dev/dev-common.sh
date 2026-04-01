#!/usr/bin/env bash

set -euo pipefail

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

info() {
  echo "$*"
}

resolve_dotnet_bin() {
  local dotnet_bin=""

  if command -v dotnet >/dev/null 2>&1; then
    dotnet_bin="$(command -v dotnet)"
  elif [[ -x "/root/.dotnet/dotnet" ]]; then
    dotnet_bin="/root/.dotnet/dotnet"
  else
    fail "dotnet was not found on PATH and /root/.dotnet/dotnet does not exist."
  fi

  readlink -f "${dotnet_bin}"
}

readonly DEV_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly REPO_ROOT="$(cd "${DEV_DIR}/.." && pwd)"
readonly PIDFILE="${DEV_DIR}/.local-dev-server.pid"
readonly LOGFILE="${DEV_DIR}/local-dev-server.log"
readonly PORT="5000"
readonly PROJECT_PATH="SaveFW.Server/SaveFW.Server.csproj"
readonly SERVER_WORKDIR="${REPO_ROOT}/SaveFW.Server"
readonly SERVER_BINARY="${REPO_ROOT}/SaveFW.Server/bin/Debug/net10.0/SaveFW.Server"
readonly DOTNET_BIN="$(resolve_dotnet_bin)"
readonly DOTNET_HOME="$(dirname "${DOTNET_BIN}")"

build_watch_cmd() {
  local interactive_mode="$1"

  local cmd=(
    env
    ASPNETCORE_ENVIRONMENT=Development
    DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER=1
    DOTNET_ROOT="${DOTNET_HOME}"
    PATH="${DOTNET_HOME}:${PATH}"
    "${DOTNET_BIN}"
    watch
  )

  if [[ "${interactive_mode}" == "background" ]]; then
    cmd+=(--non-interactive)
  fi

  cmd+=(
    run
    --project
    "${PROJECT_PATH}"
    --urls
    "http://0.0.0.0:${PORT}"
  )

  printf '%s\0' "${cmd[@]}"
}

process_exists() {
  local pid="$1"
  [[ -n "${pid}" ]] && kill -0 "${pid}" 2>/dev/null
}

cmdline_for_pid() {
  local pid="$1"
  tr '\0' ' ' < "/proc/${pid}/cmdline" 2>/dev/null || true
}

cwd_for_pid() {
  local pid="$1"
  readlink -f "/proc/${pid}/cwd" 2>/dev/null || true
}

pgid_for_pid() {
  local pid="$1"
  ps -o pgid= -p "${pid}" 2>/dev/null | tr -d '[:space:]'
}

pid_is_containerized() {
  local pid="$1"
  local cmdline
  cmdline="$(cmdline_for_pid "${pid}")"
  if [[ "${cmdline}" =~ docker|containerd|podman|docker-compose|kubepods|libpod ]]; then
    return 0
  fi

  if [[ -r "/proc/${pid}/cgroup" ]] && grep -Eq 'docker|containerd|podman|kubepods|libpod' "/proc/${pid}/cgroup"; then
    return 0
  fi

  return 1
}

pid_is_repo_dev_process() {
  local pid="$1"
  process_exists "${pid}" || return 1
  pid_is_containerized "${pid}" && return 1

  local cwd cmdline
  cwd="$(cwd_for_pid "${pid}")"
  cmdline="$(cmdline_for_pid "${pid}")"

  [[ -n "${cwd}" && "${cwd}" == "${REPO_ROOT}"* ]] || return 1
  if [[ "${cmdline}" == *"dotnet watch"* || "${cmdline}" == *"dotnet-watch.dll"* ]]; then
    [[ "${cmdline}" == *"${PROJECT_PATH}"* ]] || return 1
    return 0
  fi

  if [[ "${cmdline}" == *"${SERVER_BINARY}"* ]]; then
    return 0
  fi

  if [[ "${cmdline}" == *"dotnet run --no-launch-profile"* ]]; then
    [[ "${cwd}" == "${SERVER_WORKDIR}"* ]] || return 1
    [[ "${cmdline}" == *"--urls http://0.0.0.0:${PORT}"* ]] || return 1
    return 0
  fi

  return 1
}

load_pidfile() {
  [[ -f "${PIDFILE}" ]] || return 1
  PID=""
  PGID=""
  MODE=""
  PIDFILE_REPO_ROOT=""
  while IFS='=' read -r key value; do
    case "${key}" in
      PID) PID="${value}" ;;
      PGID) PGID="${value}" ;;
      MODE) MODE="${value}" ;;
      REPO_ROOT) PIDFILE_REPO_ROOT="${value}" ;;
    esac
  done < "${PIDFILE}"
  [[ -n "${PID}" ]]
}

write_pidfile() {
  local pid="$1"
  local pgid="$2"
  local mode="$3"
  cat > "${PIDFILE}" <<EOF
PID=${pid}
PGID=${pgid}
MODE=${mode}
REPO_ROOT=${REPO_ROOT}
EOF
}

remove_pidfile() {
  rm -f "${PIDFILE}"
}

cleanup_stale_pidfile() {
  if ! load_pidfile; then
    return 0
  fi

  if process_exists "${PID:-}"; then
    return 0
  fi

  remove_pidfile
}

assert_pidfile_matches_repo() {
  load_pidfile || fail "No local dev pidfile found at ${PIDFILE}."
  if [[ -n "${PIDFILE_REPO_ROOT:-}" && "${PIDFILE_REPO_ROOT}" != "${REPO_ROOT}" ]]; then
    fail "Pidfile repo root mismatch."
  fi
  process_exists "${PID:-}" || fail "Recorded local dev pid ${PID:-missing} is not running."
  pid_is_containerized "${PID}" && fail "Refusing to touch pid ${PID}: it appears to be a Docker/container process."
  pid_is_repo_dev_process "${PID}" || fail "Refusing to touch pid ${PID}: it does not match this repo's recorded local dev watcher."
}

list_port_pids() {
  ss -ltnp "( sport = :${PORT} )" 2>/dev/null \
    | grep -o 'pid=[0-9]\+' \
    | cut -d= -f2 \
    | sort -u
}

assert_safe_to_start() {
  cleanup_stale_pidfile

  if load_pidfile && process_exists "${PID:-}"; then
    assert_pidfile_matches_repo
    fail "Local dev process already running with pid ${PID}. Use dev/dev-stop.sh or dev/dev-restart.sh."
  fi

  local port_pids
  port_pids="$(list_port_pids || true)"
  if [[ -z "${port_pids}" ]]; then
    return 0
  fi

  while IFS= read -r pid; do
    [[ -n "${pid}" ]] || continue
    if pid_is_containerized "${pid}"; then
      fail "Port ${PORT} is owned by containerized pid ${pid}. Refusing to touch Docker/container targets."
    fi
    if pid_is_repo_dev_process "${pid}"; then
      fail "Port ${PORT} is already in use by this repo's dev process pid ${pid}. Use dev/dev-stop.sh or dev/dev-restart.sh."
    fi
    fail "Port ${PORT} is already in use by unmanaged pid ${pid}. Refusing to stop it automatically."
  done <<< "${port_pids}"
}

stop_recorded_process() {
  assert_pidfile_matches_repo

  local pid="${PID}"
  local pgid="${PGID:-}"

  if [[ -n "${pgid}" ]]; then
    kill -TERM -- "-${pgid}" 2>/dev/null || true
  else
    kill -TERM "${pid}" 2>/dev/null || true
  fi

  for _ in $(seq 1 5); do
    if ! process_exists "${pid}"; then
      remove_pidfile
      return 0
    fi
    sleep 1
  done

  if [[ -n "${pgid}" ]]; then
    kill -KILL -- "-${pgid}" 2>/dev/null || true
  else
    kill -KILL "${pid}" 2>/dev/null || true
  fi

  for _ in $(seq 1 5); do
    if ! process_exists "${pid}"; then
      remove_pidfile
      return 0
    fi
    sleep 1
  done

  fail "Timed out waiting for local dev process pid ${pid} to exit after managed escalation."
}
