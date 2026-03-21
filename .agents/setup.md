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

## Database Seeding
The backend (`SaveFW.Server`) is configured to automatically apply EF Core migrations and seed TIGER (Census) data on startup via a fire-and-forget background task. It also pre-warms the MVT tile cache for the initial map view.
