# Frontend UI/UX Standard

Status: mandatory for production UI routes. Blueprint-only simulator screens may use their dedicated visual language, but production work centers must use this standard.

## Mandatory design skill

All frontend, UI and UX work in this repository MUST use **UI UX Pro Max** before implementation or visual refactoring:

- Skill source: `nextlevelbuilder/ui-ux-pro-max-skill`
- Repository: https://github.com/nextlevelbuilder/ui-ux-pro-max-skill
- Use it for design-system decisions, layout patterns, visual hierarchy, accessibility, responsive behavior, interaction states and anti-pattern checks.
- Treat its output as design guidance, then reconcile it with this repository's domain constraints, existing production tokens, permissions, concurrency/idempotency rules and read-only Vercel demo behavior.
- Do not introduce a new visual style, component pattern, color system, typography pattern or interaction convention without first checking the skill guidance and the shared production UI standard.
- Before delivery, apply the skill's pre-delivery checks, especially visible focus states, contrast, responsive behavior, reduced-motion considerations, non-clipping labels/badges and consistent icon usage.

For this ERP/WMS project, the skill is a **required input to every frontend/UI/UX task**, not an optional reference.

## Resolved design direction

Read `design-system/erp-wms/MASTER.md` before changing production UI.

The verified UI UX Pro Max profile for this project is:
- Product: **Inventory & Stock Management**.
- Style: **Flat Design + Minimalism & Swiss Style**.
- Dashboard: **Real-Time Monitoring + Data-Dense**.
- Palette: industrial slate + stock green + semantic traffic-light status colors.
- Typography: professional, clean hierarchy; Inter primary.
- Motion: subtle, ~150ms state feedback only.
- Anti-patterns: excessive decoration, complex shadows, 3D effects, tiny body text.

## Goals

- One visual language across master data, inbound, outbound, inventory control and administration.
- Reusable patterns so new modules do not invent their own table/form/button styles.
- Dense desktop operations **without sacrificing responsive/mobile accessibility**.
- Clear distinction between read-only demo runtime and real backend runtime.

## Foundation

Shared files:

- `src/ui/production-ui.css`: design tokens and scoped production theme.
- `src/ui/ProductionUi.tsx`: reusable page primitives.
- `src/config/productionNavigation.ts`: canonical production route metadata and menu groups.
- `src/layouts/MainLayout.tsx`: application shell, route context, demo-runtime notice.

The theme is scoped by the `.production-ui` class. Do not add generic global rules for production pages outside that scope.

## Required page structure

New production pages should start with:

```tsx
<UiPage>
  <UiPageHeader
    eyebrow="Inbound"
    title="Tên work center"
    description="Một câu mô tả mục đích nghiệp vụ."
    actions={...}
  />

  <UiToolbar>
    <UiToolbarField label="Kho">...</UiToolbarField>
    <UiToolbarField label="Tìm kiếm">...</UiToolbarField>
  </UiToolbar>

  <UiCard title="Danh sách">
    ...
  </UiCard>
</UiPage>
```

## Canonical patterns

### Page header

Every production route needs:
- module/group eyebrow,
- short screen title,
- one-sentence business description,
- only primary page-level actions on the right.

Do not repeat environment/demo information inside individual pages. The application shell owns that notice.

### Filters

Use `UiToolbar` and `UiToolbarField`.
- Keep filters above the data card.
- Labels are always visible.
- Reset/filter actions sit in the same toolbar.
- Do not use placeholder text as the only label.

### Tables

- Use semantic `table/thead/tbody`.
- First column should usually contain the business identifier/code.
- Use `UiBadge` for state instead of raw status text when practical.
- Empty results must render an explicit message row or `UiEmptyState`.
- Pagination belongs immediately below the table inside the same card.
- Row actions stay in the last column.

### Forms

- Editing/creating belongs in `UiCard`.
- Use visible labels for business-critical fields.
- Primary action is submit; cancel is secondary.
- Mutation requests must preserve existing concurrency/idempotency rules.
- Demo runtime must never pretend a write succeeded.

### Status badges

Use:
- `success`: completed, active, match, approved.
- `warning`: in progress, waiting, attention.
- `danger`: exception, mismatch, rejected, failed.
- `neutral`: draft, inactive, informational.

Do not encode meaning by color alone; badge text remains mandatory.

### Empty/loading/error

Every data-driven page must have:
- loading state,
- explicit empty state,
- recoverable error message,
- no stale detail panel after a failed reload.

### Demo runtime

The Vercel Blueprint demo is read-only:
- GET is served through the controlled demo adapter.
- POST/PUT/PATCH/DELETE must fail closed.
- The shell shows the demo-runtime notice.
- Production pages must not add their own contradictory environment banner.

### Navigation

All production routes must be registered in `productionNavigation.ts` with:
- route,
- label,
- section,
- description,
- read permission/access rule.

Sections currently are:
- Vận hành
- Dữ liệu nền
- Inbound
- Outbound
- Inventory Control
- Kiểm soát

New sections require explicit review instead of ad-hoc sidebar insertion.

## Visual rules

- Use the shared spacing, radius, border and semantic color tokens from `production-ui.css`.
- Production surfaces follow **flat/minimal** styling: borders over decorative shadows.
- Avoid raw browser-default tables/buttons/forms.
- Avoid hard-coded one-off colors unless representing a reviewed domain status not covered by tokens.
- Operational body text is 14px on desktop and scales to 16px on narrow/mobile layouts.
- Labels/metadata should normally stay at 11–12px minimum; do not use 8–10px body copy.
- Use tabular figures for inventory quantities, prices, durations and operational counters.
- Keep major work areas inside flat cards/panels.
- Wide tables scroll inside their own container on small screens; do not cause page-level horizontal overflow.
- Use Lucide icons consistently; decorative icons beside visible text must be hidden from the accessibility tree.

## Accessibility

- Inputs/selects need visible labels or an appropriate accessible name.
- All interactive controls need visible focus states.
- Include a skip-to-main-content link and preserve logical keyboard order.
- Route changes should move focus to the main content region.
- Alerts use `role="alert"`; loading/success states use `role="status"`.
- Do not use color as the only indicator of state.
- Icon-only buttons need descriptive accessible names.
- Respect `prefers-reduced-motion`.
- Document language is Vietnamese (`lang="vi"`).
- Mobile navigation must expose expanded state, a clear close action and Escape dismissal.

## Definition of done for a new frontend work center

A production UI capability is not complete until:

1. Route is registered.
2. Navigation metadata exists.
3. Page uses shared production primitives or conforms to the scoped production theme.
4. Loading, error and empty states exist.
5. Read permissions are enforced.
6. Mutation permissions/concurrency/idempotency are preserved where applicable.
7. Demo runtime behavior is explicit and fail-closed for writes.
8. Tests cover the primary render and at least one important interaction/error path.
9. Vercel production build is READY.
10. CI lint/test/build passes.
11. Responsive behavior is checked against the 480 / 768 / 1024 / 1440 breakpoint system.
12. Reduced-motion and keyboard/focus behavior remain correct.
