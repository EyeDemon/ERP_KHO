// @vitest-environment jsdom
import { render, cleanup } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { UiBadge, UiCard, UiEmptyState, UiMetric, UiMetricGrid, UiPage, UiPageHeader, UiToolbar, UiToolbarField } from './ProductionUi';

describe('Production UI primitives', () => {
  afterEach(cleanup);

  it('renders the canonical page, toolbar, card and metrics structure', () => {
    const view = render(
      <UiPage>
        <UiPageHeader eyebrow="Inbound" title="Receiving" description="Receive stock." />
        <UiToolbar><UiToolbarField label="Kho"><select aria-label="Kho"><option>HCM</option></select></UiToolbarField></UiToolbar>
        <UiMetricGrid><UiMetric value={4} label="Open tasks" /></UiMetricGrid>
        <UiCard title="Danh sách"><UiBadge tone="warning">In progress</UiBadge></UiCard>
      </UiPage>
    );

    expect(view.getByText('Inbound')).toBeTruthy();
    expect(view.getByText('Receiving')).toBeTruthy();
    expect(view.getByText('Open tasks')).toBeTruthy();
    expect(view.getByText('In progress').className).toContain('warning');
  });

  it('renders a reusable empty state', () => {
    const view = render(<UiEmptyState title="Không có dữ liệu" detail="Hãy thay đổi bộ lọc." />);
    expect(view.getByText('Không có dữ liệu')).toBeTruthy();
    expect(view.getByText('Hãy thay đổi bộ lọc.')).toBeTruthy();
  });
});
