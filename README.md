<div align="center">

  <picture>
    <img alt="Save Northeast Indiana Logo" src="SaveNEIN.Client/wwwroot/assets/SAVENEIN.svg" width="200">
  </picture>

  # Save Northeast Indiana

  ### Protect Our Future

  <p>A public-information and research platform for evaluating proposed casino impacts in Northeast Indiana.</p>

  <p>
    <a href="https://dotnet.microsoft.com/"><img src="https://img.shields.io/badge/.NET_10-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET 10" /></a>
    <a href="https://learn.microsoft.com/en-us/dotnet/csharp/"><img src="https://img.shields.io/badge/C%23-239120?style=for-the-badge&logo=c-sharp&logoColor=white" alt="C#" /></a>
    <a href="https://dotnet.microsoft.com/apps/aspnet/web-apps/blazor"><img src="https://img.shields.io/badge/Blazor_WASM-512BD4?style=for-the-badge&logo=blazor&logoColor=white" alt="Blazor WebAssembly" /></a>
    <a href="https://developer.mozilla.org/docs/Web/JavaScript"><img src="https://img.shields.io/badge/JavaScript-F7DF1E?style=for-the-badge&logo=javascript&logoColor=black" alt="JavaScript" /></a>
    <a href="https://tailwindcss.com/"><img src="https://img.shields.io/badge/Tailwind_CSS-38B2AC?style=for-the-badge&logo=tailwind-css&logoColor=white" alt="Tailwind CSS" /></a>
    <a href="https://maplibre.org/"><img src="https://img.shields.io/badge/MapLibre_GL_JS-396CB2?style=for-the-badge&logo=openstreetmap&logoColor=white" alt="MapLibre GL JS" /></a>
    <a href="https://www.chartjs.org/"><img src="https://img.shields.io/badge/Chart.js-FF6384?style=for-the-badge&logo=chartdotjs&logoColor=white" alt="Chart.js" /></a>
    <a href="https://www.postgresql.org/"><img src="https://img.shields.io/badge/PostgreSQL_18-4169E1?style=for-the-badge&logo=postgresql&logoColor=white" alt="PostgreSQL 18" /></a>
    <a href="https://postgis.net/"><img src="https://img.shields.io/badge/PostGIS_3.6-005C84?style=for-the-badge&logo=postgresql&logoColor=white" alt="PostGIS 3.6" /></a>
    <a href="https://github.com/valhalla/valhalla"><img src="https://img.shields.io/badge/Valhalla-FF6F00?style=for-the-badge&logo=openstreetmap&logoColor=white" alt="Valhalla" /></a>
    <a href="https://archivebox.io/"><img src="https://img.shields.io/badge/ArchiveBox-4B5563?style=for-the-badge&logo=archivebox&logoColor=white" alt="ArchiveBox" /></a>
    <a href="https://www.docker.com/"><img src="https://img.shields.io/badge/Docker-2496ED?style=for-the-badge&logo=docker&logoColor=white" alt="Docker" /></a>
    <a href="https://nginx.org/"><img src="https://img.shields.io/badge/Nginx-009639?style=for-the-badge&logo=nginx&logoColor=white" alt="Nginx" /></a>
  </p>

</div>

## Overview

SaveNEIN presents public-facing research, fact checks, polling, crime and safety information, and the economic case against the proposed casino. It also contains a data-intensive economic-impact workbench that uses server-side routing, versioned inputs, and stored model results.

The public site is built as a Blazor WebAssembly client served by an ASP.NET Core host. JavaScript is used where browser-native libraries are the right tool: interactive maps, charts, share actions, layout behavior, and related client-side integrations.

## Current capabilities

- Reusable, JSON-backed fact-check cards with ratings, source material, share images, and social sharing.
- Archived claimant-source preservation through ArchiveBox, with capture metadata and browser-facing archive links.
- Interactive MapLibre maps backed by PostGIS data and Valhalla drive-time routing.
- A versioned economic-impact model and generated reports; the public location-to-analysis workflow is currently gated while its inputs are reconnected.
- Coalition registration plus a daily Zoho email digest for configured recipients.

## Repository structure

```text
SaveNEIN/
├── SaveNEIN.Client/                 Blazor WebAssembly client
│   ├── Components/                  Reusable UI components
│   ├── Layout/                      Application layouts
│   ├── Models/                      Client contracts and fact-check models
│   ├── Pages/                       Public pages and economic-impact workflow
│   ├── Services/                    Client-side content services
│   ├── Styles/                      Tailwind source styles
│   └── wwwroot/                     Assets, data, CSS, and JavaScript modules
├── SaveNEIN.Server/                 ASP.NET Core host and API
│   ├── Configuration/               Typed configuration options
│   ├── Controllers/                 HTTP APIs
│   ├── Data/                        DbContext, migrations, and data initialization
│   ├── Migrations/                  Entity Framework migrations
│   ├── Pages/                       Server-rendered document shell and social metadata
│   ├── Services/                    Archive, email, maps, reports, and model services
│   └── Workers/                     Background processing
├── SaveNEIN.Server.Tests/           Server test suite
├── SaveNEIN.Shared/                 Contracts shared by client and server
├── deploy/                          Production Compose, Nginx, certificates, and systemd assets
├── docs/                            Migrations, operations notes, plans, and runbooks
├── infra/valhalla/                  Valhalla configuration and generated-data locations
├── scripts/                         Development and validation tooling
├── compose.development.yml          Development app Compose configuration
├── compose.development.env.example  Development environment template
├── Dockerfile                       Application image build
└── SaveNEIN.sln                     Solution entry point
```

CloudBeaver is not part of the current application or Compose setup.

## Development

### Prerequisites

- .NET SDK 10
- Node.js 20+
- Docker Desktop or Docker Engine for containerized development
- Access to the configured development services, if using this repository's VPS-backed development setup

### Development Compose

`compose.development.yml` starts only the application container. It deliberately connects through private SSH tunnels to the VPS development database (`savefw_dev`) and Valhalla service rather than creating a second local database or routing stack.

1. Copy [`deploy/.env.example`](deploy/.env.example) to the Git-ignored `deploy/.env` and set the private credentials.
2. Copy [`compose.development.env.example`](compose.development.env.example) to the Git-ignored `.env.development`.
3. Ensure the local SSH tunnels for Postgres and Valhalla are running.
4. Start the app:

```bash
docker compose --env-file deploy/.env --env-file .env.development \
  -f compose.development.yml up --build -d
```

The development site is then available at `http://localhost:5000`.

### Local watch mode

For native local development, use the provided watcher after supplying the same private connection and Valhalla environment variables:

```bash
npm run dev
```

Other useful commands:

```bash
npm run dev:start
npm run dev:status
npm run dev:stop
npm run dev:restart
dotnet test SaveNEIN.Server.Tests/SaveNEIN.Server.Tests.csproj
```

Tailwind compilation and vendored-library copying are integrated into the .NET client build.

## Production

Production uses [`deploy/compose.production.yml`](deploy/compose.production.yml), not the development Compose file. It runs the application, PostgreSQL/PostGIS, Valhalla, Nginx, ArchiveBox, and certificate-maintenance services on an isolated Docker network.

Private settings, credentials, database passwords, email recipients, and ArchiveBox tokens belong only in the Git-ignored `deploy/.env`. See the [production deployment workflow](docs/runbooks/PRODUCTION_DEPLOYMENT_WORKFLOW.md) before deploying.

## Data and source integrity

Model inputs are versioned and sealed before finalized analysis runs reference them. Claimant websites cited by fact checks can be captured through ArchiveBox so the public record identifies the actual capture date rather than treating a live page as immutable historical evidence.

## License

- **Base platform and web application:** [GNU Affero General Public License v3.0](LICENSE.md)
- **Advanced economic modeling subsystem:** [PolyForm Noncommercial License 1.0.0](LICENSE-MODEL.md)

<div align="center">
  <p><em>A volunteer effort by concerned residents of Northeast Indiana.</em></p>
  <p><a href="https://savenein.com"><strong>Visit the live site</strong></a></p>
</div>
