# SaveNEIN Architecture

The project is a multi-tier, full-stack application leveraging the .NET 10 ecosystem, Docker containerization, and self-hosted spatial analysis tools.

## High-Level Components
1. **SaveNEIN.Client**: A Blazor WebAssembly frontend. Runs entirely in the browser. Uses Tailwind CSS for styling and MapLibre GL JS for rendering interactive maps.
2. **SaveNEIN.Server**: An ASP.NET Core Web API backend serving the frontend assets (`app.UseBlazorFrameworkFiles()`) and providing RESTful APIs. It handles heavy operations like database querying, MVT (Vector Tile) generation using PostGIS, and data ingestion (TIGER/Census data).
3. **SaveNEIN.Shared**: A class library containing data models (e.g., `Legislator.cs`, `ImpactFact.cs`, `SliderMarker.cs`) shared between the Client and Server.
4. **Database (savenein-db)**: A PostgreSQL 18 instance with the PostGIS 3.6 extension for geospatial data storage and querying.
5. **Valhalla (valhalla)**: A self-hosted open-source routing engine running in a separate container, used for generating offline drive-time polygons (isochrones).
6. **CloudBeaver (savenein-db-gui)**: An optional web-based GUI for database management.

## System Interaction
- The Client makes HTTP requests to the Server for data and vector tiles.
- The Server communicates with the Postgres database via Entity Framework Core (`Npgsql.EntityFrameworkCore.PostgreSQL.NetTopologySuite`).
- The Server makes internal HTTP requests to the Valhalla container to generate drive-time polygons.
- On startup, the Server runs background tasks to automatically seed the database with US Census (TIGER) data and warm up the MVT tile cache.
