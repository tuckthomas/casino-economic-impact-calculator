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
   This starts the .NET dev server on port 5000 with hot reload via `dotnet watch`.

> **Note:** Frontend asset building (`npm install`, `copy-libs`, `build:css`) is handled automatically by MSBuild pre-build targets in `SaveFW.Client.csproj`. No separate build step is needed.

## Agent Process Hygiene

When working in local dev mode with `npm run dev` / `dotnet watch`, agents must treat the watcher as a singleton process for this repo and port.

- Before starting a watcher, check whether `SaveFW.Server` or `dotnet watch` is already listening on port `5000`.
- Never start a second watcher on the same port just because the previous command output is not attached to the current session.
- If a restart is needed, stop the existing watcher first, confirm the port is free, then start exactly one new watcher.
- Prefer verifying the running server with `ss -ltnp`, `ps -ef`, and `curl` before launching another process.
- Do not run `dotnet build SaveFW.Server/SaveFW.Server.csproj` in parallel with an active watcher unless the watcher is stopped first; Blazor boot asset generation can fail on locked files.
- If the browser is requesting stale hashed `_framework` assets, first suspect duplicate/stale watcher processes and clear them before doing anything else.

## Database Seeding
The backend (`SaveFW.Server`) is configured to automatically apply EF Core migrations and seed TIGER (Census) data on startup via a fire-and-forget background task. It also pre-warms the MVT tile cache for the initial map view.
