import apiClient from './apiClient';

export type TraceabilityWarehouse = {
  id: number;
  code: string;
  name: string;
};

/** Only the traceability permission-scoped endpoint may populate this selector. */
export async function getTraceabilityWarehouses(): Promise<TraceabilityWarehouse[]> {
  const response = await apiClient.get<TraceabilityWarehouse[]>('/api/inventory/traceability-warehouses');
  if (!Array.isArray(response.data) || !response.data.every(x =>
    x && Number.isSafeInteger(x.id) && x.id > 0 &&
    typeof x.code === 'string' && typeof x.name === 'string'
  )) {
    throw new Error('Danh sách kho truy vết không hợp lệ.');
  }
  return response.data;
}
