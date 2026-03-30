# Instructions for AI Coding Agent: Add Dynamic Charting to Net Economic Impact Cost Model

## Objective

Enhance the existing **Net Economic Impact** section by adding a **dynamic chart that updates in real time** as the user adjusts the already-linked **AGR** and **Tax Revenue** controls.

Important: the current page is already functioning primarily as a **social cost model**, not just a revenue model. The existing linked behavior between AGR and tax revenue should remain intact. Do **not** introduce new independent sliders for tax revenue, wages, or other derived values.

The goal is to improve readability and interpretation of the model outputs, not to add more user-controlled assumptions that could break internal consistency.

---

## Current State

The page already has:

- an **AGR slider**
- a **Tax Revenue slider/display**
- linked behavior where changing one updates the other in real time
- a detailed **Net Economic Impact table**
- category-level balances including:
  - Public Health (Humanitarian)
  - Public Health (Taxpayer)
  - Law Enforcement
  - Social Services
  - Civil Legal
  - Abused Dollars
  - Lost Employment
  - subtotals and total net economic impact

Because AGR and tax are already linked, these should remain the **only primary scenario controls** in this portion of the UI.

---

## Recommendation Summary

### Add charting, not more inputs

Do **not** add:
- a separate gross gambling wages slider
- a separate independent tax revenue slider
- other direct manual override sliders unless they are hidden behind an advanced/debug mode

Instead, add **output visualizations** that react to the current model state.

---

## Phase 1: Add Main Dynamic Chart

### Chart type
Add a **diverging horizontal bar chart** that visualizes the same category-level results currently shown in the table.

### Purpose
This chart should help users instantly understand:
- which categories are net positive
- which categories are net negative
- whether the total result is actually being driven by public-sector positives while private-sector harms pull the model down

### Categories to include
Use the existing table categories, at minimum:

- Public Health (Humanitarian)
- Public Health (Taxpayer)
- Law Enforcement
- Social Services
- Civil Legal
- Abused Dollars
- Lost Employment
- Total Net Economic Impact

Optionally include subtotal rows if the visual remains clean:
- Subtotal: Public Health
- Subtotal: General Taxpayer Services
- Subtotal: Public Sector Impact
- Subtotal: Private Sector Impact

### Behavior
Each bar should:
- extend **right** for positive values
- extend **left** for negative values

### Data modes
Provide a toggle to switch the chart between:

- **Steuben County Net Balance**
- **Indiana Net Balance**

If easy, optionally include:
- **County Costs only**
- **County + Other Indiana Counties spillover**

### Tooltips
On hover, show:
- category name
- exact dollar value
- whether it is positive or negative

### Placement
Place this chart **above the Net Economic Impact table**, directly beneath the section heading or explanatory copy.

---

## Phase 2: Add Sensitivity Chart

After the diverging bar chart is working, add a second chart.

### Chart type
Add a **line chart** showing how total net impact changes as AGR changes.

### Purpose
This chart should make it easy for users to see:
- how sensitive the model is to revenue assumptions
- where the model crosses breakeven
- whether higher AGR actually rescues the project or whether social costs continue to offset gains

### Axes
- **X-axis:** AGR
- **Y-axis:** Net Economic Impact

### Series
At minimum, allow:
- Steuben County Net
- Indiana Net

Optional:
- Public Sector Net
- Private Sector Net

### Live marker
Show the **current selected AGR point** on the line so users can see where the live scenario sits relative to the broader curve.

### Placement
Place this chart **below the table**.

---

## UI / Interaction Rules

### Keep existing linked model behavior
The current linked relationship between AGR and Tax Revenue should remain unchanged.

If the user moves AGR:
- tax updates automatically
- table updates automatically
- charts update automatically

If the user moves tax:
- AGR updates automatically
- table updates automatically
- charts update automatically

### Do not add new independent controls
Do not add separate normal-user controls for:
- wages
- payroll
- tax allocation overrides
- cost bucket overrides

Those would create internally inconsistent scenarios unless carefully gated behind advanced settings.

---

## Visual / UX Guidance

### Styling
Match the current application styling:
- dark theme
- clean modern financial-dashboard look
- consistent spacing
- readable labels
- no clutter

### Chart color logic
Use a consistent positive/negative visual scheme:
- positive = green/teal family
- negative = red/coral family

Keep this aligned with the table’s existing color semantics if already established in the UI.

### Responsiveness
Charts should:
- resize properly for desktop
- remain readable without breaking layout
- avoid overly dense labels on smaller widths

### Accessibility
Ensure:
- tooltip text is readable
- labels are not too small
- color is not the only signal for positive vs negative
- values can still be interpreted without hover if possible

---

## Data Mapping Requirements

Use the same underlying computed values already driving the current net impact table.

Do **not** duplicate business logic in the chart layer.

The chart components should consume the already-computed model outputs, ideally from a shared state object or selector.

If a transformation layer is needed, create a small adapter that maps current model output into chart-friendly arrays.

---

## Implementation Priorities

### Priority 1
Implement the **diverging horizontal bar chart** tied to the existing live model state.

### Priority 2
Implement the **AGR sensitivity line chart**.

### Priority 3
Add polish:
- mode toggles
- current-point annotation
- subtotal inclusion/exclusion
- improved legends
- animated transitions if smooth and not distracting

---

## Preferred Development Approach

### Reuse current computed state
Use whatever existing state/store/computed object is already being used to render:
- AGR values
- tax values
- net impact table rows

### Avoid logic duplication
Do not re-derive category balances separately in the chart component if the table already has them.

### Keep charts as visualization components
The charts should be downstream consumers of the model, not new sources of truth.

---

## Acceptance Criteria

### Diverging bar chart
- updates live when AGR changes
- updates live when tax changes
- shows category-level positive and negative balances
- supports at least County vs Indiana mode
- uses the same model outputs as the table

### Sensitivity chart
- shows total net impact across AGR values
- displays the current selected scenario on the curve
- clearly shows whether/where breakeven occurs
- updates correctly when model assumptions change

### General
- existing linked AGR/tax behavior remains intact
- no new independent normal-user sliders are introduced
- visual design matches the rest of the application
- charting improves interpretation without replacing the table

---

## Core Principle

This section is currently a **cost model with linked AGR/tax controls**.

The correct enhancement is:

- **keep the existing controls**
- **add dynamic visual outputs**
- **help users interpret the consequences of the current scenario in real time**

Do not turn this into a loose sandbox full of disconnected sliders.