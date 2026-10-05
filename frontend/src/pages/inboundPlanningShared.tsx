import type { Dispatch, MutableRefObject, SetStateAction } from 'react';
import apiClient from '../services/apiClient';
import { completeIdempotentAction, idempotencyHeaders } from '../services/idempotency';
import { permissionError } from '../services/permissionPresentation';
import { UiMetric, UiMetricGrid, UiPageHeader, UiToolbar, UiToolbarField } from '../ui/ProductionUi';

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

type VersionedInboundDetail={rowVersion?:string};

export async function runInboundStateCommand<T extends VersionedInboundDetail>({
  selected,path,action,successMessage,errorMessage,mutationLock,
  setBusy,setError,setSuccess,setSelected,reload,
}:{
  selected:T|null;path:string;action:string;successMessage:string;errorMessage:string;
  mutationLock:MutableRefObject<Set<string>>;
  setBusy:Dispatch<SetStateAction<string>>;
  setError:Dispatch<SetStateAction<string>>;
  setSuccess:Dispatch<SetStateAction<string>>;
  setSelected:Dispatch<SetStateAction<T|null>>;
  reload:()=>Promise<void>;
}) {
  if(!selected?.rowVersion||mutationLock.current.has(action))return;
  mutationLock.current.add(action);setBusy(action);setError('');setSuccess('');
  try{
    const response=await apiClient.post<T>(path,{rowVersion:selected.rowVersion},{headers:idempotencyHeaders(action)});
    completeIdempotentAction(action);
    setSelected(response.data);setSuccess(successMessage);await reload();
  }catch(err:unknown){
    if((err as {response?:{status?:number}})?.response?.status===409)setSelected(null);
    setError(permissionError(err,errorMessage));
  }finally{
    mutationLock.current.delete(action);setBusy('');
  }
}

export function InboundPlanningOverview({
  title,description,error,success,metrics,labels,
  statusFilter,setStatusFilter,search,setSearch,placeholder,
}:{
  title:string;
  description:string;
  error:string;
  success:string;
  metrics:Array<{value:number;label:string}>;
  labels:Record<string,string>;
  statusFilter:string;
  setStatusFilter:Dispatch<SetStateAction<string>>;
  search:string;
  setSearch:Dispatch<SetStateAction<string>>;
  placeholder:string;
}) {
  return <>
    <UiPageHeader eyebrow="Nhập kho" title={title} description={description}/>
    <InboundPlanningFeedback error={error} success={success}/>
    <UiMetricGrid>
      {metrics.map(metric=><UiMetric key={metric.label} value={metric.value} label={metric.label}/>)}
    </UiMetricGrid>
    <InboundPlanningToolbar
      labels={labels}
      statusFilter={statusFilter}
      setStatusFilter={setStatusFilter}
      search={search}
      setSearch={setSearch}
      placeholder={placeholder}
    />
  </>;
}
