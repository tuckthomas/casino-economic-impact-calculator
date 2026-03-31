# Setup & Local Development

## Prerequisites
- **Docker** and **Docker Compose** are required to run the full stack locally.
- **Hardware**: Minimum 8GB RAM (16GB recommended) is required, specifically because the **Valhalla** routing engine needs significant memory to "cook" the graph tiles for the initial build.

## Running the Application (Docker)
1. Clone the repository.
2. From the root directory (`/root/SaveFW/`), run:
   ```bash
   docker compose up --build -d
   ```
3. Access the services:
   - **Web App**: http://localhost:80
   - **CloudBeaver (DB GUI)**: http://localhost:8978
   - **Valhalla API**: http://localhost:8002

## Local Development (.NET)
If you need to work on the .NET code without running the app entirely in Docker:
1. Ensure the .NET 10 SDK is installed.
2. Ensure the database and Valhalla containers are running (`docker compose up -d savefw-db valhalla`).
3. Start the dev server with hot reload:
   ```bash
   npm run dev
   ```
   This starts the repo-managed local dev watcher on port 5000 with hot reload via `dotnet watch`.
4. Use the repo-managed process scripts for local dev lifecycle:
   ```bash
   npm run dev:start
   npm run dev:stop
   npm run dev:restart
   npm run dev:status
   ```
   `npm run dev` is the foreground hot-reload path. The `dev:start` / `dev:restart` scripts run a repo-managed background local server with pidfile safety checks.

> **Note:** Frontend asset building (`npm install`, `copy-libs`, `build:css`) is handled automatically by MSBuild pre-build targets in `SaveFW.Client.csproj`. No separate build step is needed.

## Agent Process Safety Rules

When working in local dev mode, agents must use the repo-managed scripts in `/root/SaveFW/dev/` and must not freestyle process control.

- Allowed commands:
  - `npm run dev`
  - `npm run dev:start`
  - `npm run dev:stop`
  - `npm run dev:restart`
  - `npm run dev:status`
  - direct invocation of the matching `dev/dev-*.sh` scripts
- Forbidden behavior:
  - do not use ad hoc `kill`, `pkill`, `killall`, or broad process-name matching to manage local dev
  - do not stop processes by port owner unless that pid matches this repo's recorded pidfile
  - do not touch Docker, Docker Compose, Podman, containerd, kubepods, or any containerized process when fixing local dev
  - do not stop any `SaveFW.Server`, `dotnet`, `npm`, or `node` process unless it is the exact pid recorded by `/root/SaveFW/dev/.local-dev-server.pid`
- The `dev/dev-stop.sh` and `dev/dev-restart.sh` scripts are the only approved stop/restart path for the local watcher.
- If the local app serves stale or missing hashed `_framework` assets, use the repo-managed restart flow only. Do not kill unrelated processes.
- If port `5000` is occupied by an unmanaged or containerized process, agents must refuse to stop it automatically and report that fact instead.
- Do not run `dotnet build SaveFW.Server/SaveFW.Server.csproj` in parallel with an active watcher unless the watcher is first stopped through the repo-managed scripts.

## Database Seeding
The backend (`SaveFW.Server`) is configured to automatically apply EF Core migrations and seed TIGER (Census) data on startup via a fire-and-forget background task. It also pre-warms the MVT tile cache for the initial map view.
