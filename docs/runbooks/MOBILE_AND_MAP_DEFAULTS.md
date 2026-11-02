# Mobile interaction and optional regional map startup

`mobile-interactions.js` provides a modal bottom sheet below 768px for triggers with
`data-mobile-select="native-select-id"` and `data-mobile-label="Picker name"`.
The hidden native select remains the source of options and emits the existing change
event, so calculator values and Blazor state follow the same path as desktop inputs.
The sheet supports filtering long lists, disabled options, keyboard navigation,
Escape/backdrop dismissal, focus restoration, and safe-area padding. Native visible
selects retain the browser's platform picker.

WebHaptics 0.0.6 is pinned in both existing lockfiles. `pnpm run copy-libs` copies its
ESM files and MIT license locally; no CDN is required. Trusted touch interactions
use subtle selection feedback, with debug audio disabled. Reduced-motion settings
disable haptics. Add `data-haptics="off"` to a container to disable them there.
Actual vibration depends on the device, browser and operating-system settings;
visual feedback remains available without vibration.

## Remove the SaveNEIN-specific map start

Remove the `northeast-indiana-startup.js` script tag from both:

- `SaveNEIN.Server/Pages/Index.cshtml` (hosted application)
- `SaveNEIN.Client/wwwroot/index.html` (standalone client)

The map will then start at its generic national state-selection view. No changes
to the reusable map implementation are needed. The optional script exposes
`NortheastIndianaMapStartup.apply(api)` and listens for `impact-map-ready` once per
map instance. It selects state FIPS 18, outlines Steuben (18151), DeKalb (18033),
and Allen (18003) in the same gold, adds a light gold fill and high-contrast county-name labels, and frames northeast Indiana without selecting a county or
placing a casino. Generic `impact-map-navigation` events hide these highlights
when the user returns to national selection or chooses another state. They never
reapply the initial zoom or override the back button.

The gold layers, label positions, and label styles are all defined inside that optional script. Labels allow pointer events through to the county selection beneath them.
