# SaveFW Tech Stack

## Frontend (SaveFW.Client)
- **Framework**: Blazor WebAssembly (.NET 10)
- **Styling**: Tailwind CSS (compiled via CLI, offline-first approach)
- **Maps**: MapLibre GL JS (GPU-accelerated vector maps), Turf.js (geospatial analysis)
- **Charting**: Chart.js
- **PDF Generation**: html2canvas, QuestPDF (backend)
- **Offline Protocol**: PMTiles.js

## Backend (SaveFW.Server)
- **Framework**: ASP.NET Core Web API (.NET 10)
- **ORM**: Entity Framework Core 10
- **Database Provider**: Npgsql (PostgreSQL) with NetTopologySuite (PostGIS)
- **PDF Generation**: QuestPDF
- **Background Tasks**: .NET Worker Services for data ingestion and cache warming

## Database & Infrastructure
- **Database**: PostgreSQL 18 + PostGIS 3.6
- **Routing Engine**: Valhalla (offline isochrone generation)
- **Containerization**: Docker & Docker Compose
- **Database Management**: CloudBeaver

## Design Philosophy
- **Offline-First / Zero External Dependencies**: All critical CSS, JavaScript, fonts, and map tiles are vendored or served locally to ensure stability and privacy.
