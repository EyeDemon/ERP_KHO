# ERP_KHO Staging UAT Matrix

Status: `PENDING DEVELOPER ACCEPTANCE VERIFICATION / STAGING ONLY / PRODUCTION NO-GO`

Run this matrix only against an approved isolated staging environment with
synthetic or explicitly approved test data. Record actor, timestamp UTC,
correlation ID, document code and expected/actual result. Do not use
`scripts/seed.sql` or `scripts/seed2.sql`.

| ID | Area | Scenario | Expected result | Evidence / sign-off |
| --- | --- | --- | --- | --- |
| AUTH-01 | Authentication | Valid login | Access token issued; refresh cookie has approved flags | Pending |
| AUTH-02 | Authentication | Refresh | Token rotates; old refresh token cannot be reused | Pending |
| AUTH-03 | Authentication | Logout | Refresh session is revoked; response contains no secret | Pending |
| AUTH-04 | Authentication | Revoke one/all sessions | Selected session(s) become invalid | Pending |
| WH-01 | Isolation | User assigned warehouse A reads A | Allowed | Pending |
| WH-02 | Isolation | Same user reads warehouse B | Not found/denied per policy | Pending |
| WH-03 | Isolation | Transfer checker has source only | Approval denied | Pending |
| WH-04 | Isolation | Transfer checker has both warehouses | Approval allowed when not maker | Pending |
| IMP-01 | Import | Create Draft import | Draft created; no inventory movement | Pending |
| IMP-02 | Import | Manager/Admin approves import | Inventory and one ledger movement update atomically | Pending |
| IMP-03 | Import | Reject import | Draft -> Cancelled; `ApprovalRejected`; no inventory movement | Pending |
| EXP-01 | Export | Create Draft export | Draft created; no premature movement | Pending |
| EXP-02 | Export | Approve with reservation mode | Reservation updates atomically; expected state/audit | Pending |
| EXP-03 | Export | Dispatch approved export | OnHand/reservation/ledger update exactly once | Pending |
| EXP-04 | Export | Cancel Draft/Approved according to configured mode | Correct reservation release and audit; no duplicate movement | Pending |
| TRF-01 | Transfer | Create Draft transfer | Valid source/destination and no movement | Pending |
| TRF-02 | Transfer | Approve as maker | Denied, including Admin maker | Pending |
| TRF-03 | Transfer | Approve as checker with both scopes | Approved once with audit | Pending |
| TRF-04 | Transfer | Dispatch, receive, complete | State sequence and inventory movements reconcile | Pending |
| RES-01 | Reservation | Reserve available stock | Active reservation; `ReservedQuantity` increases safely | Pending |
| RES-02 | Reservation | Release reservation | Reservation released; counters reconcile | Pending |
| RES-03 | Reservation | Expiry handling | Expired reservation is cleaned only by approved caller | Pending |
| RES-04 | Reservation | Reconciliation | No negative/over-reserved or ledger mismatch | Pending |
| STK-01 | Stocktake | Create and record count | Draft stocktake with synthetic count | Pending |
| STK-02 | Stocktake | Approve valid count | Adjustment and audit match expected delta | Pending |
| STK-03 | Stocktake | Approve below reserved quantity | Conflict; reservation invariant preserved | Pending |
| APR-01 | Approval | Queue/detail/history | Only scoped documents appear; pagination/filter totals match | Pending |
| APR-02 | Approval | Approval Aging | Draft pending items show UTC-derived Normal/Warning/Overdue | Pending |
| APR-03 | Approval | Maker attempts approve/reject | Denied; no success audit or business mutation | Pending |
| APR-04 | Approval | Same idempotency key replay | Stable response; no duplicate mutation/audit | Pending |
| APR-05 | Approval | Same key with different payload | Conflict; original result remains authoritative | Pending |

## UAT exit criteria

- All applicable rows PASS or have a developer-recorded exception.
- No inventory, reservation, ledger, warehouse-isolation or audit mismatch.
- No unresolved P0 finding.
- Correlation IDs and logs are retained without secrets.
- The solo developer records the technical, operations, database/rollback and
  deployment decision in the self-review documents.

UAT PASS does not authorize production. It is one input to the release-gate
decision.
