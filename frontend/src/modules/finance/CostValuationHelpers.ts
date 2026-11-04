import { CANONICAL_WH01_LABEL, formatWarehouseLabel } from '../../utils/warehouseLabel';
import type { FinanceSummaryState, ValuationRow } from './CostValuationWacAndMisaSection';

export const extractCostErrorText = (err: any, fallback: string) => {
  const data = err?.response?.data;
  const code = data?.errorCode || data?.code || err?.response?.status || '';
  const corr = err?.response?.headers?.['x-correlation-id'] || data?.correlationId || '';
  const msg = data?.message || data?.title || fallback;
  return `${msg}${code ? ` [${code}]` : ''}${corr ? ` (CorrelationId: ${corr})` : ''}`;
};

export const mapRawValuationItems = (rawItems: unknown[], hidden: boolean): ValuationRow[] =>
  rawItems.map((raw: any, idx: number) => {
    const item = (raw ?? {}) as Record<string, unknown>;
    const onHandQty = Number(item.onHandQty ?? item.onHandQuantity ?? item.quantity ?? 0);
    const availableQty = Number(item.availableQty ?? item.availableQuantity ?? onHandQty);
    const unitCost = hidden
      ? 0
      : Number(
          item.movingAverageUnitCost ??
            item.movingAverageCost ??
            item.unitCost ??
            item.averageCost ??
            item.costPrice ??
            0,
        );
    const totalValue = hidden
      ? 0
      : Number(
          item.totalInventoryValue ??
            item.totalValuation ??
            item.totalCostValue ??
            item.inventoryValue ??
            Math.round(onHandQty * unitCost),
        );
    return {
      productId: Number(item.productId ?? item.id ?? idx + 1),
      productCode: String(item.productCode ?? item.sku ?? item.code ?? `MH-${idx + 1}`),
      productName: String(item.productName ?? item.name ?? 'Mặt hàng tồn kho'),
      warehouseName:
        formatWarehouseLabel(
          String(item.warehouseName ?? ''),
          String(item.warehouseCode ?? ''),
          Number(item.warehouseId ?? 1),
        ) || CANONICAL_WH01_LABEL,
      unitName: String(item.unitName ?? item.uomName ?? item.uomCode ?? 'Cái'),
      onHandQty,
      availableQty,
      movingAverageUnitCost: unitCost,
      totalInventoryValue: totalValue,
    };
  });

export function parseOwnerGrossProfitSummary(d: Record<string, unknown>, hidden: boolean): FinanceSummaryState {
  const grossRevenue = Number(d.grossRevenue ?? d.netRevenue ?? d.totalDispatchedRevenue ?? 0);
  const movingAverageCogs = hidden ? 0 : Number(d.movingAverageCogs ?? d.totalCogs ?? d.cogsAmount ?? 0);
  const computedGrossProfit = Math.max(0, grossRevenue - movingAverageCogs);
  const rawNetPocketProfit = Number(
    d.netPocketProfit ?? d.estimatedGrossProfit ?? d.grossProfit ?? computedGrossProfit,
  );
  const grossProfitBeforeFees = hidden ? 0 : computedGrossProfit > 0 ? computedGrossProfit : rawNetPocketProfit;
  const grossMarginBeforeFeesPct = hidden
    ? 0
    : grossRevenue > 0
      ? Number(((grossProfitBeforeFees / grossRevenue) * 100).toFixed(2))
      : Number(d.grossMarginPercent ?? d.grossMarginPct ?? d.netMarginPct ?? 0);
  const netPocketProfit = hidden ? 0 : rawNetPocketProfit;
  const shippingAndPlatformFee = hidden
    ? 0
    : Number(
        d.shippingAndPlatformFee ??
          d.platformAndShippingFee ??
          Math.max(0, grossProfitBeforeFees - netPocketProfit),
      );
  const netMarginPct = hidden
    ? 0
    : Number(
        d.netMarginPct ??
          d.grossMarginPercent ??
          d.grossMarginPct ??
          (grossRevenue > 0 ? Number(((netPocketProfit / grossRevenue) * 100).toFixed(2)) : 0),
      );
  const withheldTaxTotalAmount = Number(
    d.withheldTaxTotalAmount ?? d.totalWithheldTaxAmount ?? Math.round(grossRevenue * 0.015),
  );
  const fixedInfrastructureFeeTotal = Number(
    d.fixedInfrastructureFeeTotal ?? d.totalFixedOrderProcessingFee ?? (grossRevenue > 0 ? 3000 : 0),
  );
  const netProfitAfterWithheldTax = hidden
    ? 0
    : Number(
        d.netProfitAfterWithheldTax ??
          Math.max(
            0,
            grossProfitBeforeFees -
              shippingAndPlatformFee -
              withheldTaxTotalAmount -
              fixedInfrastructureFeeTotal,
          ),
      );

  return {
    grossRevenue,
    movingAverageCogs,
    grossProfitBeforeFees,
    grossMarginBeforeFeesPct,
    shippingAndPlatformFee,
    netPocketProfit,
    netMarginPct,
    withheldTaxTotalAmount,
    fixedInfrastructureFeeTotal,
    netProfitAfterWithheldTax,
  };
}
