# Instructions: Fix “NEW ADDICTS” double-counting by modeling **net new** (marginal) prevalence

## Goal
Update the dashboard so it clearly distinguishes:

1. **Total estimated problem gamblers (post-casino, by tier)**, and  
2. **Net new problem gamblers attributable to the proposed casino (by tier)**

Right now the UI labels *total prevalence* as **NEW ADDICTS**, which overstates incremental impact (especially the 20–50 mile tier).

This change makes the model defensible: “new” = **delta above the pre-casino counterfactual**.

---

## High-level changes (what to build)
### A) Keep the existing “baseline prevalence” slider, but reinterpret it as **PRE-casino prevalence**
- Current slider appears to control a single prevalence rate (e.g., 2.3%), which then gets multiplied by tier risk multipliers (2.0x, 1.5x, 1.0x).
- That is a sensible approach **if**:
  - the baseline is **pre-casino** statewide prevalence, and
  - you compute *post-casino* tier rates via multipliers, and
  - you report **net new** as the delta above baseline.

✅ Recommendation: **Do not add a second slider unless you truly want separate pre/post baselines.**  
In most cases, one baseline slider is enough.

### B) Replace/augment output fields
For each tier card, show BOTH:

- **TOTAL ESTIMATED PROBLEM GAMBLERS** = Population × PostRate  
- **NET NEW (ATTRIBUTABLE)** = Population × (PostRate − PreRate)

Where:
- `PreRate` = baseline slider value
- `PostRate` = `PreRate × multiplier` (or whatever your tier logic uses)

For the baseline tier with multiplier = 1.0:
- `PostRate = PreRate`
- `NetNew = 0`

---

## Calculation spec (exact formulas)

### Inputs
- `preRate` (decimal) = baseline prevalence slider (e.g., 0.023 for 2.3%)
- For each tier `t`:
  - `multiplier[t]` (e.g., 2.0, 1.5, 1.0)
  - `populationTotal[t]`
  - `populationCounty[t]`
  - `populationOther[t]`

### Derived
For each tier `t`:

1) **PostRate**
```text
postRate[t] = clamp(preRate * multiplier[t], 0, 1)
```

2) **TotalEstimated**
```text
totalEstimatedTotal[t]  = round(populationTotal[t]  * postRate[t])
totalEstimatedCounty[t] = round(populationCounty[t] * postRate[t])
totalEstimatedOther[t]  = round(populationOther[t]  * postRate[t])
```

3) **NetNew (Attributable)**
```text
deltaRate[t] = max(0, postRate[t] - preRate)
netNewTotal[t]  = round(populationTotal[t]  * deltaRate[t])
netNewCounty[t] = round(populationCounty[t] * deltaRate[t])
netNewOther[t]  = round(populationOther[t]  * deltaRate[t])
```

Notes:
- `max(0, …)` prevents negative deltas if someone sets a multiplier < 1.0 by accident.
- Keep using your existing “County vs Other Counties” split exactly the same way.

### Display rate on the card
- Keep showing `postRate[t]` as the tier rate (since that’s the total prevalence expected post-casino *within that tier*).
- Also show `deltaRate[t]` optionally (recommended) as “Added Risk”.

Example label:
- **Rate (Post): 4.6%**
- **Added (Delta): +2.3%**

---

## UI changes (concrete)
### 1) Slider label + presets
**Rename** the slider label to something unambiguous, like:

- “Pre-casino problem gambling prevalence (baseline)”

If you have “Diagnosed / Problem / At-Risk” markers, either:
- rename them to match what they truly represent (if you have correct definitions), or
- remove them and replace with a **dropdown preset** list.

Minimum viable: keep slider, remove misleading tick labels.

### 2) Tier cards
Replace “NEW ADDICTS” with two rows:

- **TOTAL ESTIMATED**
- **NET NEW (ATTRIBUTABLE)**

Keep the county/other breakdown under each, same structure you already have.

### 3) Summary totals (if you have a top-line KPI)
If you display a single top-line number today (e.g., “NEW ADDICTS”), update it to either:

- **NET NEW (ATTRIBUTABLE) — Total** (recommended for advocacy accuracy)
or show both:
- **TOTAL ESTIMATED — Total**
- **NET NEW (ATTRIBUTABLE) — Total**

### 4) Baseline tier expectations
For the 20–50 mile tier (multiplier 1.0):
- **Net new should always be 0**
- Total estimated should still compute (that’s just baseline prevalence applied to that population)

This prevents the credibility hit where you appear to claim 45-mile residents are “new addicts because of the casino”.

---

## Optional: add a second slider (only if you really need it)
If you want to support a model where the statewide baseline itself changes after the casino opens (usually unnecessary), then add:

- `preRate` slider = pre-casino prevalence
- `postBaseRate` slider = “post-casino baseline prevalence” (applied to multiplier 1.0 tier)

Then:
- baseline tier uses `postBaseRate`
- elevated tiers use `postBaseRate × multiplier`
- net new uses delta relative to `preRate`

But again: **this is usually overcomplicating without better data.**

---

## Implementation checklist (Gemini-friendly)
1) Identify where the baseline slider state is stored (likely a React state or store).
2) Rename variables for clarity:
   - `baselineRate` -> `preRate`
3) Update tier calculations:
   - compute `postRate`, `deltaRate`, `totalEstimated`, `netNew`
4) Update tier card UI text:
   - replace “NEW ADDICTS” with “TOTAL ESTIMATED” and “NET NEW (ATTRIBUTABLE)”
5) Update any aggregate totals accordingly.
6) Add basic unit tests / quick console asserts:
   - multiplier 1.0 => netNew == 0 for any population
   - multiplier 2.0, preRate 0.023, pop 236,666:
     - deltaRate = 0.023
     - netNew ≈ 5,443 (since 236,666 × 0.023 = 5,443.318)

---

## Acceptance criteria
- Changing the baseline slider changes both:
  - Total estimated counts (post)
  - Net new counts (delta)
- Baseline tier (1.0x) always shows:
  - Net new = 0
- Labels match the math:
  - “Total estimated” is not described as “new”
  - “Net new” clearly indicates “attributable / incremental”
