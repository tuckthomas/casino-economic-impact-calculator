# Data Source Priorities for SaveFW

This is a revised and more tightly prioritized version of the source list for SaveFW.

The original list was directionally strong, but it treated some contextual layers as being too close in importance to the legal, fiscal, and site-feasibility layers that actually drive the model.

For this project, the data sources fall into three tiers:

1. Legal boundary and fiscal-rule layers
2. Site-feasibility and market-benchmark layers
3. Contextual socioeconomic overlays

---

## Core principle

The most important question is not just "what looks good on a map."

It is:

- whether a site qualifies for municipal revenue allocation
- whether a site is realistically developable
- whether a site is competitively advantaged
- whether projected revenue assumptions are grounded in real Indiana market behavior

That means legal boundaries, fiscal rules, site plausibility, access, and actual casino benchmarks matter more than generalized demographic context.

---

## Highest-priority data sources

### 1. Current municipal boundaries / annexation-aware incorporated-place layer

This is the most immediate and most important mapping need.

You need to know whether a proposed casino site is actually inside:
- Fort Wayne
- Angola
- Auburn
- New Haven
- or unincorporated territory

That is a hard legal trigger for local tax allocation.

Recommended sources:
- Indiana statewide incorporated municipal boundaries
- Allen County / local GIS for current boundary validation
- TIGER `PLACE` as a fallback or national baseline

### 2. Indiana / local finance allocation data

This is just as important as the map layer.

The problem is not only where a site sits geographically. It is also:
- who receives which share of tax revenue
- whether a municipality qualifies for the city allocation
- what happens in unincorporated territory
- how local, county, and regional distributions are actually structured

Recommended sources:
- Indiana state finance publications
- DLGF
- Indiana Gateway
- statute-specific fiscal notes and distribution language

### 3. Zoning, parcel ownership, land use, annexation, and site constraints

This should be ranked very high, but for a more precise reason than "zoning might stop the casino."

In Allen County / Fort Wayne, zoning may not always be a hard barrier. Some commercial districts may already be permissive enough, and politically connected officials could try to rezone a parcel if they want a site badly enough.

So zoning should not be treated as a binary veto. It should be treated as part of a broader site-feasibility analysis.

A site may look appealing on a map and still be less viable than it appears because it may:
- be outside current municipal limits
- require rezoning or other land-use approvals
- face adjacency conflicts
- have drainage or floodplain issues
- lack adequate utility capacity
- have weak ingress / egress
- involve parcel assembly problems
- be politically harder to advance even if technically possible

Recommended sources:
- county assessor parcel data
- zoning maps
- land-use maps
- local planning agencies
- annexation history
- utility and infrastructure data where available

### 4. Indiana Gaming Commission revenue, admissions, and wagering data

This is one of the strongest benchmarking datasets available.

It allows the model to compare projected casino performance against actual Indiana market outcomes rather than consultant assumptions or promoter claims.

Recommended uses:
- calibrating expected revenue ranges
- benchmarking performance by market type
- checking whether destination-style claims are credible
- comparing nearby competitive effects and substitution patterns

### 5. Roads, ramps, interchange access, and traffic counts

Roads should no longer be treated as merely visual map context.

For basic legal/fiscal allocation logic, they are secondary. But once the model starts projecting site-level revenue, transportation access becomes a much more important explanatory variable.

A site that is closer to a major highway interchange, has cleaner ingress / egress, and sits near higher-volume traffic corridors may be materially better positioned to capture gambling, dining, and incidental visitation than a site farther off-network.

For revenue modeling, the most useful transportation variables are likely to include:
- distance to nearest interstate
- distance to nearest major highway exit
- distance to nearest ramp or interchange
- nearby AADT traffic volumes
- functional class of surrounding roads
- number of lanes / facility type
- drive-time from exit to parcel rather than straight-line distance alone

Recommended sources:
- INDOT traffic count data
- INDOT roadway inventory
- interchange / ramp geometry
- local road network layers
- corridor traffic datasets

---

## Second-tier contextual and analytic layers

### 6. ACS demographic data at tract or block-group level

These are useful for contextual overlays and localized burden analysis.

Potential uses:
- income
- poverty
- disability
- age
- education
- household composition

This is helpful for showing which communities may absorb more harm or be more vulnerable, but it is not more important than legal boundaries, finance rules, site plausibility, and market benchmarks.

### 7. ACS commuting mode and travel-time fields

Useful for understanding:
- realistic labor-shed relationships
- practical cross-county movement
- whether nearby counties are meaningfully connected to a site beyond simple distance

### 8. LEHD / LODES origin-destination flows

This is stronger than distance alone for modeling regional labor-market relationships and spillover assumptions.

Useful for:
- employment catchments
- cross-county commuting
- testing whether "regional draw" claims line up with real movement patterns

### 9. QCEW / County Business Patterns / BEA local area income data

These are useful economic baseline layers.

Potential uses:
- existing wage conditions
- local employment structure
- hospitality / entertainment sector baselines
- personal income context
- substitution-effect framing

These are useful, but they should sit below the legal, fiscal, access, and site-feasibility layers.

---

## Additional important layers missing from the original list

### Election precincts and past vote results

This is important if SaveFW is intended to support persuasion, targeting, or political strategy in addition to analysis.

Potential uses:
- identifying likely support / opposition zones
- tailoring local messaging
- connecting geography to actual political behavior

### Taxing districts, TIF districts, and redevelopment areas

These can materially affect how infrastructure and fiscal impacts are perceived and allocated.

Potential uses:
- understanding whether a site sits inside a tax-capture framework
- clarifying who really bears infrastructure costs
- identifying where fiscal narratives may be distorted by redevelopment tools

### Fire, EMS, police response areas, and station locations

This is more useful than generic public-service references.

Potential uses:
- tying modeled burden to actual emergency-service coverage
- highlighting local systems likely to absorb increased demand
- illustrating jurisdictional mismatch between tax receipts and service burden

### Hospital, addiction-treatment, and mental-health provider capacity

Provider locations alone are not enough.

A point on a map does not tell you:
- available treatment capacity
- whether providers are already strained
- how far affected populations would need to travel
- whether expected burden aligns with actual local service infrastructure

### Courts, jails, and public-health service locations

Still useful, especially when trying to connect abstract social-cost estimates to real institutions.

### Building permits, site plans, rezoning petitions, BZA cases, and plan commission agendas

These are high-value sources if the goal is to track whether a "hypothetical" site is quietly becoming real.

Potential uses:
- identifying early development movement
- detecting entitlement activity
- connecting rumors or land activity to actual public filings

### Existing casino competitor layer with locations, drive times, and amenity metadata

This is especially important for SaveFW.

Potential uses:
- measuring competitive overlap
- testing substitution assumptions
- comparing destination strength
- identifying whether a proposed site has any real advantage versus existing regional casinos

### Hotel inventory and lodging tax data

This is not core unless tourism claims become central.

If project advocates are arguing that the casino will function as a destination draw, hotel capacity and local lodging patterns become more relevant evidence.

---

## Lower-priority or optional layers

### `COUSUB`

Useful only if you later want township or county-subdivision reporting.

Not nearly as important as municipal boundaries for the allocation question.

### Urban area polygons

Helpful for messaging and visual context, but not especially important for the core model.

### School district boundaries

Potentially useful for a broader public-systems discussion, but not a core input for the current site and fiscal analysis.

### Fine-grained block polygons / `TABBLOCK20`

Can be useful for edge-case population allocation or very fine-grained spatial modeling, but easy to overbuild.

In most cases, tract or block-group geography is likely sufficient.

### Generic roads shapefile without traffic or interchange context

A plain road centerline layer is not enough by itself for revenue modeling.

It becomes much more useful only when paired with:
- ramps / interchange geometry
- traffic volume
- facility type
- travel-time logic

---

## Revised priority order

1. Current municipal boundaries / annexation-aware incorporated-place layer
2. Indiana / local finance allocation data
3. Zoning, parcel ownership, land use, annexation, and site-feasibility constraints
4. Indiana Gaming Commission historical market-performance data
5. Roads, ramps, interchange access, and traffic counts
6. ACS tract or block-group demographic data
7. LEHD / LODES commuting flows
8. QCEW / CBP / BEA baseline economic context
9. Emergency-service and treatment-capacity layers
10. Political / redevelopment / permit-tracking layers

---

## Must-have / should-have / nice-to-have

### Must-have
- Current municipal boundaries
- Annexation-aware local GIS
- Indiana / local finance allocation data
- Zoning / parcel / land-use constraints
- Indiana Gaming Commission performance data
- Road / interchange / traffic access data

### Should-have
- ACS tract or block-group demographics
- LEHD / LODES commuting flows
- QCEW / CBP / BEA baseline economic data
- Emergency-service coverage
- Treatment-provider capacity
- Permit / rezoning / development-review data
- Competitor casino layer

### Nice-to-have
- `COUSUB`
- Urban area polygons
- School districts
- Fine-grained block-level population allocation
- Other contextual map layers

---

## Final recommendation

The original source list was not bad. It just needed stronger prioritization and a more realistic framing of what actually drives the model.

The most important correction is this:

Do not treat all map layers as equal.

For SaveFW, the model should be built first around:
- legal municipal boundaries
- revenue allocation rules
- real-world site feasibility
- transportation access
- actual Indiana casino benchmark data

Everything else should support those layers, not compete with them.