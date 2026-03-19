# SaveFW Key Features

The Save Fort Wayne platform provides several data-driven tools to analyze and visualize the impact of proposed casino expansions:

1. **Economic Impact Calculator**
   - An interactive financial model auditing "net benefit" claims.
   - Users can adjust variables like Adjusted Gross Revenue (AGR), tax allocations, and social cost multipliers.
   - Calculates the true projected community deficit based on academic formulas (Grinols, Welte).

2. **Economic Impact Simulator**
   - A guided wizard for exploring "What If" scenarios.
   - Compares conservative revenue estimates against developer claims, factoring in social cost sensitivity.

3. **Interactive Slot Machine**
   - A digital slot machine used as a visual metaphor for the "Near Miss" psychological effect in gambling.

4. **Impact Zone Visualizer**
   - A MapLibre GL JS-based interactive map displaying the geographic scope of problem gambling.
   - Uses **Valhalla** to generate precise 1-hour drive-time polygons (isochrones).
   - Backend dynamically generates and serves Mapbox Vector Tiles (MVT) via PostGIS `ST_AsMVT`.

5. **Detailed Demographics & Claim Analysis**
   - Granular population data for Indiana counties.
   - Side-by-side comparison of marketing claims versus documented realities supported by independent studies.
