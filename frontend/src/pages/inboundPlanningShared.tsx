import type { Dispatch, SetStateAction } from 'react';
import { UiToolbar, UiToolbarField } from '../ui/ProductionUi';

type ProductUnit = { unitId:number; unitCode:string; unitName:string };
type ProductWithUnits = {
  id:number;
  unitId:number;
  unitCode:string;
  unitName:string;
  uoms?: ProductUnit[];
};

export const productUnitOptions = (products:ProductWithUnits[], productId:number|'') => {
  const product=products.find(item=>item.id===productId);
  if(!product)return [];
  const base:ProductUnit={unitId:product.unitId,unitCode:product.unitCode,unitName:product.unitName};
  return [base,...(product.uoms??[])]
    .filter((item,index,array)=>array.findIndex(candidate=>candidate.unitId===item.unitId)===index);
};

export function InboundPlanningFeedback({error,success}:{error:string;success:string}) {
  return <>
    {error&&<div role="alert">{error}</div>}
    {success&&<p role="status" className="ui-success-text">{success}</p>}
  </>;
}

export function InboundPlanningToolbar({
  labels,
  statusFilter,
  setStatusFilter,
  search,
  setSearch,
  placeholder,
}:{
  labels:Record<string,string>;
  statusFilter:string;
  setStatusFilter:Dispatch<SetStateAction<string>>;
  search:string;
  setSearch:Dispatch<SetStateAction<string>>;
  placeholder:string;
}) {
  return <UiToolbar>
    <UiToolbarField label="Trạng thái">
      <select className="inbound-planning-status-filter" value={statusFilter} onChange={event=>setStatusFilter(event.target.value)}>
        <option value="">Tất cả trạng thái</option>
        {Object.entries(labels).map(([value,label])=><option key={value} value={value}>{label}</option>)}
      </select>
    </UiToolbarField>
    <UiToolbarField label="Tìm trong danh sách">
      <input className="inbound-planning-search" value={search} onChange={event=>setSearch(event.target.value)} placeholder={placeholder}/>
    </UiToolbarField>
  </UiToolbar>;
}
