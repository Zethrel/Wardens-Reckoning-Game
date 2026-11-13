# Warden's Reckoning — UI Usability Improvement Plan

**Document type:** Implementation planning proposal  
**Status:** Proposed; not adopted or implemented  
**Last updated:** 13-11-2026  
**Suggested destination:** `._Wardens_Reckoning_Implementation_planning/` (UI/UX planning)

## 1. Purpose

Improve the usability of the existing prototype interface without committing to a full visual redesign. The immediate goal is to make the simulation easier to observe, explain and control while preserving development time for core gameplay systems.

**Primary pain point:** The colony screen's large, permanently visible dark-grey inspector/sidebar occupies a substantial portion of the 1280 × 720 window, even when the player would benefit more from seeing the colony. Reclaiming this space is the first priority.

## 2. Guiding principles

- **Function before decoration:** improve information hierarchy and screen-space use now; postpone a comprehensive art pass.
- **World view first:** the colony should remain the main focus. Information panels should appear when useful, not dominate the screen permanently.
- **Progressive disclosure:** show essential information immediately; place detailed statistics and diagnostics in expandable sections or tooltips.
- **Explainable simulation:** make it easy to learn why a colonist is idle, travelling, interrupting work or selecting an activity.
- **Deterministic separation:** UI reads authoritative simulation state and submits commands through defined interfaces; UI rendering and panel state do not change simulation outcomes.
- **Incremental implementation:** deliver and test small improvements rather than rebuilding the whole interface at once.

## 3. Immediate priorities

### UI-01 — Reclaim colony viewport space

**Priority: Highest**

Replace the permanently wide right-hand information area with a more flexible inspector.

Proposed behaviour:

- When no colonist or object is selected, the inspector should collapse or hide, leaving the colony view as wide as practical.
- When a colonist is selected, show a compact inspector with their name, current activity, schedule and most relevant needs.
- Provide a clear control to expand the inspector for full details, and to collapse it again.
- Consider an overlay or slide-out inspector rather than reserving its full width permanently. Test which arrangement feels least intrusive during play.
- Preserve selection when the panel is collapsed; reopening should show the same selection if it remains valid.
- Avoid obscuring important controls or the selected colonist. Keep the map usable at the target 1280 × 720 resolution.
- If resizing is implemented, use sensible minimum and maximum widths. A draggable splitter is optional, not required for the first iteration.

**Acceptance checks:** The colony fills the reclaimed area when the inspector is closed; selecting a colonist makes essential information accessible in one action; closing and reopening the inspector does not change simulation state.

### UI-02 — Organise the colonist inspector

**Priority: High**

Replace the continuous text list with a clear information hierarchy:

1. **Identity and status:** name, current schedule block, whether working, available, resting or otherwise occupied.
2. **Current activity:** plain-language activity and, where useful, destination or reason.
3. **Needs:** compact, clearly labelled indicators for hunger, thirst, rest/fatigue, hygiene, recreation and social fulfilment as implemented.
4. **Skills and interests:** expandable details showing skill level and actual work pace.
5. **Detailed diagnostics:** optional technical information such as coordinates, activity state and scheduling data.

**Important:** The current display uses values such as `0 of 100, fine`; determine and label whether the values represent *need deficits* or *satisfaction*. Do not accidentally reverse their meaning in progress bars.

### UI-03 — Explain colonist behaviour

**Priority: High**

Add an accessible **“Why?”** explanation for current activities and blocked tasks.

Example: A colonist scheduled to Work is “between jobs, pottering about.” Their explanation could state that no eligible work is currently available, that they are using permitted between-job time, and that successfully assigned work takes priority.

Explanations should:

- Report the actual known cause, not guess at hidden reasons.
- Distinguish *no available work*, *ineligible work*, *unreachable work* and *work claimed by someone else* when the simulation can establish the difference.
- Provide a concise initial explanation and optional deeper detail.
- Respect player knowledge and avoid exposing undiscovered information.
- Reuse the simulation's existing reason/status information where possible; avoid duplicating decision logic in the UI.

### UI-04 — Improve selection and hover feedback

**Priority: Medium–High**

- Make the selected colonist or object clearly distinguishable on the map.
- Add restrained hover highlighting and concise tooltips where useful.
- Ensure selecting another colonist updates the inspector consistently.
- Keep labels legible without covering too much terrain.
- Verify that UI interactions do not interfere with map commands.

### UI-05 — Basic notifications

**Priority: Medium**

Introduce concise notifications for actionable situations such as blocked work, shortages and urgent needs. Prefer severity and relevance over volume; avoid repeated alerts for the same unresolved condition. A notification should lead to the affected colonist, location or explanation where practical.

### UI-06 — Improve founding-site selection

**Priority: Medium**

The world-generation screen already shows a world map and a list of founding sites. Improve their connection:

- Selecting a site in the list highlights its corresponding map marker.
- Selecting a marker highlights or scrolls to the matching list entry.
- Display the selected site's available summary (such as biome and water access) clearly.
- Preserve the world map as the visual focus; do not let the list consume unnecessary width.
- Avoid implying that undiscovered or ungenerated details are known.

## 4. Recommended implementation order

| Phase | Scope | Completion criterion |
|---|---|---|
| **A — Immediate screen-space fix** | Collapsible/hideable inspector; usable colony viewport | The grey sidebar no longer permanently dominates the screen |
| **B — Inspector usability** | Grouped status, activity, needs, skills and interests | Essential colonist information is readable at a glance |
| **C — Behaviour explanations** | “Why?” details for activity and work state | Common idle and blocked states have accurate explanations |
| **D — Interaction feedback** | Selection, hover and concise notifications | Players can identify and investigate important events |
| **E — World-map selection** | Synchronised site list and map markers | Founding sites are easy to compare and locate |

Implement each phase as a small, independently testable change. The order may be adjusted if a gameplay milestone exposes a more urgent usability problem.

## 5. Later UI milestones

### After free-time and scheduling systems stabilise

- Editable daily schedules and reusable templates.
- Job priorities and assignment controls.
- More useful settlement management controls.
- Expanded information about leisure, social interactions and personal interests when those systems exist.

### After the core gameplay loops stabilise

- Consistent visual identity, typography, colours, icons and panel styling.
- A broader review of navigation and information architecture.
- Optional richer management views justified by real gameplay needs.

### Before wider playtesting

- Readability and accessibility checks.
- Keyboard navigation, focus states and tooltips.
- UI scaling and testing at different resolutions.
- Usability testing with players unfamiliar with the simulation.

## 5a. Visual direction: the Warden's Ledger (the maintainer, 13-11-2026)

Charcoal panels, parchment accents, muted brass highlights, and organised
information resembling a settlement administrator's records. A concept picture
of the colony screen in this look: `docs/ui/wardens-ledger-concept.png` (a
concept, not a specification: its buttons and portrait are not decided).

Applied as each piece of the interface is reworked, from step 26b (the
inspector); the colours are kept in one place (`src/UI/Ledger.cs`). The full
art pass stays deferred (section 6).

## 6. Explicitly deferred

Do **not** treat the following as requirements for the immediate UI improvement:

- A complete medieval/fantasy-themed visual overhaul.
- Elaborate decorative borders or animated transitions.
- Fully dockable, rearrangeable window management.
- Large statistical dashboards without demonstrated player need.
- Detailed relationship screens before the relationship systems are implemented.
- Rebuilding every existing screen at once.

## 7. Technical boundaries and verification

- Keep presentation separate from authoritative simulation logic.
- UI controls submit commands through the existing command pathway; they do not directly mutate colonist needs, jobs, schedules or world state.
- Pausing, changing simulation speed, selecting entities and opening panels must behave predictably.
- Closing a panel must not cancel or change a colonist's activity.
- Test at the current **1280 × 720** prototype window size and at least one larger resolution.
- Verify the UI remains responsive during ×5 simulation and under heavier colony workloads.
- Record before/after screenshots and any performance regressions.

## 8. Proposed decision

**Approve a limited functional UI usability pass now, beginning with the oversized right-hand inspector.** Defer the full visual overhaul until the main gameplay loops are sufficiently stable.

This document is a proposal only. It does not authorise edits to the game repository or authoritative design library without separate approval.
