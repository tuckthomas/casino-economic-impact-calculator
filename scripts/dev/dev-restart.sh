#!/usr/bin/env bash

set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/dev-common.sh"

"${DEV_DIR}/dev-stop.sh"
"${DEV_DIR}/dev-start.sh" "${@:-}"
