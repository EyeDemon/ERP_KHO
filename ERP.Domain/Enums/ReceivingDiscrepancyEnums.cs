namespace ERP.Domain.Enums;

public enum ReceivingDiscrepancyStatus { Pending, Submitted, PendingApproval, Rejected, Resolved }
public enum ReceivingResolutionAction { AcceptObserved, AcceptExpectedRejectExcess, RejectAtDoor, RejectWrongProduct, RouteToQc }
public enum ResponsibleParty { Supplier, Carrier, Warehouse, CustomerReturn, Unknown }
