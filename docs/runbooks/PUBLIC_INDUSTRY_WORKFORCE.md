# Public industry and workforce evidence

The browser calculator's industry and workforce sections provide descriptive economic context and a separate hiring illustration. They do not change Grinols costs, AGR, gaming taxes, displacement or financial balances.

## Public sources and geographic coverage

- BLS QCEW annual averages: 2025, 2020 and 2015; private ownership code 5; county workplaces. Private national employment supplies the location-quotient denominator. Employment, establishment counts, payroll and annual pay are different measures. Establishment counts can remain public when jobs/pay are suppressed. Suppressed fields stay null, even if the original CSV zero-fills them.
- Census ACS 2020–2024 five-year table-based summary files: B23025 resident labor-force measures and B08007 residents' place of work. Published 90% margins are retained. B08007 total=001, in-county=003, elsewhere in state=004, outside state=005. Categories must reconcile to resident workers. These data describe outbound commuting, not incoming workers or casino patrons. County figures are not allocated into isochrones.
- BLS LAUS: latest common monthly observation retrieved by the snapshot builder for Steuben, DeKalb and Allen counties. Not seasonally adjusted; preliminary flags retained. These denominators take precedence for recruitment-scale comparisons; ACS supplies an explicitly labeled fallback elsewhere.
- May 2025 BLS OEWS public query tables: Northern Indiana nonmetropolitan area for Steuben/DeKalb and Fort Wayne area for Allen. The official county mapping is published at https://www.bls.gov/oes/2025/may/in_counties.htm. Selected hospitality, security, production and all-occupation benchmarks retain their regional geography. Hourly medians and annual means are distinguished. Annual wages generally assume 2,080 hours, exclude benefits, and do not represent actual annual earnings for seasonal/part-time workers or promised casino pay.

ACS county coverage is nationwide. Versioned QCEW snapshots guarantee availability for the three northeast Indiana counties; other county industry profiles query the same public BLS CSV endpoints with bounded concurrency, timeouts and caching. Missing historical data and unavailable regional wage coverage are disclosed. Another region's wages are never silently substituted. No paid service, Census API credential or new database table is required.

## Updating the snapshots

Run `python scripts/refresh-public-economy.py --refresh` from the repository root. This downloads the public source files and replaces the derived snapshots in `SaveNEIN.Server/Data/PublicEconomy`. Without `--refresh`, existing raw files in the ignored `.cache/public-economy` directory are reused for reproducible processing. Actual download timestamps and SHA-256 hashes are recorded in `sources.json`; rebuilding cached inputs does not change their retrieval dates. Review source vintages before changing the explicit 2025/2020/2015/2024 periods.

The server copies the derived JSON files into build and publish outputs. The raw downloads stay outside source control. Source periods, coverage, suppression, nominal-dollar treatment and missing evidence are documented in the UI, automated analysis and PDF.

## Hiring illustration

The initial 1,000 positions, 1.00 FTE per position and four 25% recruitment shares are editing defaults, not empirical findings or an employer staffing commitment. Users can change positions, FTE factor and the mix of previously unemployed residents, existing-employer transfers, incoming commuters and relocating residents. Shares must total 100%. Supporting information is exported with the chosen assumptions. Counts are conditional expected assignments and rounded independently.

One distinct worker per position is assumed. Existing-employer transfers do not by themselves add employed residents; incoming commuters live elsewhere. New-resident assignments are not a migration forecast. The illustration does not estimate replacement vacancies, multipliers, net job creation or dollar benefits. It is excluded from the financial balance.

## Verification

`node --test scripts/validation/local-economy.test.mjs` covers assignment arithmetic, invalid inputs, monthly-versus-ACS baselines, narrative evidence, escaping, and stale county requests. The .NET `LocalEconomyProfileTests` exercise suppression, growth/LQ denominators and malformed CSV. `IndustryWorkforceReportTests` reconcile all three county snapshots and render their report exhibits. Visually inspect an actual browser export, its contents destinations, margins and table pagination before releasing layout changes.
