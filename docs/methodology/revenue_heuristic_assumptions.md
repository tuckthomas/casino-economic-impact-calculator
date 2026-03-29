# Revenue Heuristic Assumptions (Public-Data-Informed)

This note documents assumptions used by the revenue-potential layer so scenario outputs remain auditable and transparent.

## Scope

- The revenue module is a **heuristic** for relative site comparison.
- It is **not** a precise AGR forecast engine.
- It is designed to support user-run sensitivity scenarios.

## Current assumptions

1. **Primary market anchor**: Fort Wayne urban core is used as the primary market center for Northeast Indiana corridor access.
2. **Competition pressure**: Existing active casinos/racinos are weighted by venue type + feature richness and reduced by distance decay.
3. **Catchment overlap proxy**: Competitors closer to the Fort Wayne demand center receive higher overlap influence than equally distant venues outside that corridor.
4. **Market depth proxy**: Nearby block-group population is converted to an adult proxy (`population × 0.75`) and income-adjusted by median-income normalization.
5. **Benchmark normalization**: Site market depth is normalized to a benchmark depth constant representing a corridor-strength reference site.
6. **Bounded output**: Final multiplier is clamped to avoid pseudo-precision and to keep output in scenario-friendly bounds.

## Confidence framing

- Confidence is stronger for **directional ranking** (stronger/weaker location quality) than for exact dollar AGR.
- Missing state/operator ZIP-level visitation and theoretical win data materially raises uncertainty on exact AGR point values.

## ZIP-by-ZIP switching model (new API layer)

- The server now supports a ZIP-level competition switching calculation at `POST /api/revenue/zip-switching`.
- Each ZIP input contributes an estimated demand pool (`adults × participation rate × annual GGR per participant × income index`).
- Proposed-site and competitor venue shares are allocated per ZIP using a softmax utility split:
  - utility increases with venue quality
  - utility decreases with distance from ZIP centroid
- This layer is intended for scenario comparison and cannibalization sensitivity testing, not as a proprietary-grade forecast replication.
