# ERP WMS — UI UX Pro Max Master Design System

Status: authoritative visual direction for production frontend routes.

Source: UI UX Pro Max (`nextlevelbuilder/ui-ux-pro-max-skill`), verified against the Inventory & Stock Management product profile and the skill's accessibility/responsive guidance.

## Resolved product profile

- Product type: **Inventory & Stock Management**
- Primary style: **Flat Design + Minimalism & Swiss Style**
- Secondary guidance: **Accessible & Ethical**
- Dashboard direction: **Real-Time Monitoring + Data-Dense**
- Typography mood: **Professional + Clean hierarchy**
- Effects: **Color-shift hover + fast ~150ms transitions + no decorative shadows**
- Anti-patterns: excessive decoration, complex shadows, 3D effects, inconsistent styling.
- Design dials for this ERP/WMS:
  - Variance: **2/10** — centered, predictable, minimal.
  - Motion: **2/10** — subtle feedback only.
  - Density: **8/10** — dense operational dashboard, while preserving target size and readability.

## Semantic palette

Derived from the skill's `Inventory & Stock Management` palette:

| Token | Value | Use |
| --- | --- | --- |
| Primary | `#334155` | Primary actions, focus family, strong controls |
| On Primary | `#FFFFFF` | Text/icons on primary |
| Secondary | `#475569` | Secondary text and controls |
| Accent | `#059669` | Scanner/positive operational accent |
| Background | `#F8FAFC` | App canvas |
| Foreground | `#0F172A` | Primary text |
| Card | `#FFFFFF` | Work-center surfaces |
| Muted | `#F2F3F4` | Subtle surfaces |
| Muted Foreground | `#475569` | Secondary text |
| Border | `#E6E8EA` | Dividers and flat surface boundaries |
| Destructive | `#DC2626` | Destructive/error actions |
| Ring | `#334155` | Keyboard focus |

Operational status colors are traffic-light semantics and MUST include text:
- success/complete/match → green,
- waiting/in-progress/attention → amber,
- exception/rejected/mismatch → red,
- draft/inactive/informational → slate/neutral.

## Typography

- Primary UI family: **Inter**.
- Vietnamese/system fallback: `"Be Vietnam Pro", system-ui, "Segoe UI", sans-serif`.
- Numeric/data columns use tabular figures.
- Monospace is reserved for codes/technical identifiers where it improves scanning.

Scale:
- Page title: 24px desktop / 22px mobile.
- Section title: 18px.
- Card title: 16px.
- Operational body: 14px desktop; 16px on narrow/mobile layouts.
- Label / metadata: 11–12px minimum; do not use 8–10px body copy.
- Line height: 1.5 or greater for prose.

## Layout and spacing

Use a 4/8-based rhythm:
- 4, 8, 12, 16, 24, 32px.
- Desktop content max width: 1440px.
- Core breakpoints: 480 / 768 / 1024 / 1440.
- Sidebar is persistent on large screens and becomes an accessible drawer below 1024px.
- Wide operational tables may scroll **inside their table container** on small screens; the page itself must not horizontally overflow.
- Long text and identifiers must wrap safely.

## Interaction

- Primary interaction is click/tap, never hover-only.
- Color transitions: ~150ms; no decorative choreography.
- Pressed/hover/disabled/focus states must be visible without moving layout bounds.
- Web pointer targets must satisfy WCAG 2.2 target-size requirements; mobile-adjacent controls use 44px minimum height where practical.
- Async actions must expose loading and error/success feedback.
- Destructive actions remain visually distinct and separated from primary actions.
- Production demo mutations remain fail-closed.

## Navigation

- Sidebar items use **Lucide outline icons + text labels**.
- Current route uses `aria-current="page"` and a visible active state.
- Core routes are deep-linkable.
- Route changes move focus to the main content region for screen-reader/keyboard orientation.
- A skip-to-content link is required.
- Mobile drawer has an accessible open/close control and Escape dismissal.

## Accessibility

Critical requirements:
- Normal text contrast ≥4.5:1.
- Visible 3px focus indicator.
- Semantic labels for form controls.
- Icon-only controls require accessible names.
- Decorative icons beside visible text use `aria-hidden="true"`.
- Status is never color-only.
- `prefers-reduced-motion` is respected.
- HTML document language is `vi`.
- Keyboard navigation follows the visual order.
- Modal/sheet flows must provide a clear escape route.

## Component rules

Production pages use the shared primitives in `src/ui/ProductionUi.tsx` and tokens from `src/ui/production-ui.css`.

Required patterns:
- `UiPage`
- `UiPageHeader`
- `UiToolbar` / `UiToolbarField`
- `UiCard`
- `UiBadge`
- `UiEmptyState`
- `UiTableScroll`
- `UiMetricGrid` / `UiMetric`

Specialized work centers may keep domain-specific layout CSS, but colors, borders, radius, typography, status semantics and interaction states must consume shared tokens.

## Performance

- Avoid large decorative assets and expensive visual effects.
- Use route/feature splitting when feature weight warrants it.
- Lists above ~50 rows should be evaluated for virtualization or server pagination.
- Avoid unnecessary effects and memoization; follow current React guidance.
- Reserve stable space for async content to reduce layout shift.

## Pre-delivery gate

Every frontend/UI/UX change must verify:
1. UI UX Pro Max guidance was consulted for the task.
2. Master profile and any page override were read.
3. Keyboard and focus path works.
4. No unlabeled icon-only controls.
5. Contrast and status semantics remain accessible.
6. 375/768/1024/1440 layouts are considered and no page-level horizontal overflow is introduced.
7. Reduced-motion behavior is safe.
8. Loading/error/empty states are present.
9. Vercel deployment is READY.
10. CI lint/test/build passes.
