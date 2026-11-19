# ORVA Billing UI ↔ Backend Contract v1.0

**Status:** Frozen interface boundary for the Gemini backend task and Qwen WPF UI task.

**Project:** ORVA by Rindle Works — native Windows WPF, C# 7.3, .NET Framework 4.8.1, SQLite.

## 1. Why this contract exists

Gemini implements the billing domain and persistence. Qwen implements the WPF billing experience and live invoice preview. Both must use the same public contract so the work can be integrated without rewriting either module.

The C# contract file included with the source is authoritative:

`src/Orva.Desktop/Domain/Billing/BillingContracts.cs`

Do not independently redefine DTOs, rename members, change enum values, or create a second competing billing API. If a genuine blocker requires a change, document the proposed change and why before implementing it.

## 2. Public interface

```csharp
public interface IBillingService
{
    IList<MedicineLookupResult> SearchMedicines(string query, int maximumResults);
    InvoicePreviewResult PreviewInvoice(InvoiceDraft draft);
    InvoiceResult FinalizeInvoice(InvoiceDraft draft);
}
```

### SearchMedicines

- Returns active catalogue items relevant to the entered medicine text or a recognised barcode.
- Search should be fast and limited to `maximumResults`.
- A medicine can be returned with `CanSell = false` when it currently has no eligible stock, so the UI can explain availability without hiding the medicine entirely.
- `AvailableQuantityMilli` is the sum of eligible sellable quantities across batches, not a batch quantity.
- FEFO batch selection is not exposed to the UI.
- The current schema has `barcode_mapping`; barcode lookup may be integrated into this method. Do not add a barcode registration workflow.

### PreviewInvoice

- Must not write to the database, allocate an invoice number, reserve stock, or deduct stock.
- Re-reads current database state and uses the exact same calculation/allocation rules as finalisation.
- Returns line totals, tax totals, grand total, tender total, remaining amount and any blocking issues.
- If a requested quantity must be fulfilled from multiple FEFO batches, return those allocations in `BatchAllocations` for the paper preview. The staff must not choose or edit those allocations.
- While the user is still entering payment, `AmountRemainingPaise` may be positive and `CanFinalize` false. This is a normal incomplete draft, not a crash.
- `DiscountPaise` remains zero for V1; discount is not exposed as a UI feature.
- Tax configuration is resolved from saved shop/CA-approved configuration. Never ask ordinary staff to choose intra-state/inter-state, tax-inclusive/exclusive, GST rates or HSN codes during routine billing. If required configuration is missing, return a clear blocking issue such as “Tax setup needs to be completed in Settings. Please contact support.”

### FinalizeInvoice

- Revalidates the draft using fresh database reads. A preview result is not a reservation and must not be trusted as current stock.
- Creates invoice, invoice lines, payment rows, stock movements, stock deductions, invoice sequence and audit entry in one SQLite transaction.
- Payment tender total must exactly equal the final payable amount in paise for the agreed fully-paid workflow.
- Returns `Succeeded = true` only after the transaction commits.
- A post-commit StockData.json refresh failure must be a warning, not a false invoice failure.
- Does not print. Printing is a separate integration concern.

## 3. Input ownership

`InvoiceDraft` contains only:
- Medicine IDs and requested `QuantityMilli` for each medicine line.
- Optional customer name, phone, address and GSTIN.
- Zero or more payment tenders with method, exact paise amount and optional reference text.

The UI must not send authoritative prices, batch IDs, expiry dates, GST rates, HSN codes, stock quantity, tax mode, or batch status. The backend reads those from the database/configuration and returns a preview.

Payment enum values are exactly:
- `Cash`
- `Upi`
- `DebitCard`
- `CreditCard`

There is no generic “Other” tender and no unapproved discount field.

## 4. Integer units

- Money: integer paise (`long`). Convert to decimal rupees only for display formatting.
- Quantity: integer thousandths (`QuantityMilli`). Convert to a readable quantity string only for display.
- Never calculate money or stock using `double` or `float` in either layer.
- The WPF UI displays backend-returned totals. It must not duplicate tax, FEFO or rounding calculations.

## 5. Staff-facing UI rules

- Search-first; keyboard and USB keyboard-wedge barcode scanner friendly.
- Staff never select batches.
- Medicine names, unit, available quantity and price/line total are clearly visible.
- Use clear messages (“Only 3 strips are available”), not exception text or database terms.
- Do not require customer details for ordinary sales.
- Do not expose discounts, GST configuration or HSN configuration as routine billing controls.
- Split payments supported using only the four approved methods.
- Keep “Generate Invoice” disabled until the service says `CanFinalize`.
- On finalisation success, clear the draft only after displaying the committed invoice number. Show immediate next actions; animation must never delay commit.

## 6. Paper preview

Qwen may implement an on-screen live paper preview for:
- 58 mm thermal receipt
- 80 mm thermal receipt
- A4
- A5

This is only an on-screen preview. Do not implement physical printer integration in this assignment. Use backend preview data for all monetary and batch-allocation values. Preview format changes must not alter invoice totals.

## 7. Integration ownership

- Gemini owns backend implementation of `IBillingService` and database transactions.
- Qwen owns new WPF billing view/view-model/preview controls using `IBillingService`.
- ChatGPT owns integration: `AppServices`, dependency wiring, MainWindow navigation, shared project references, build fixes and final regression verification.

To prevent conflicts:
- Gemini must not edit WPF XAML, MainWindow or AppServices.
- Qwen must not edit SQL schema, repositories, billing calculations or AppServices/MainWindow.
- Neither model may modify `BillingContracts.cs` without a written proposal.

## 8. Error handling

- Expected user-correctable conditions are returned as `BillingIssue` values, not raw exceptions.
- Unexpected infrastructure failures may throw; the UI catches them, logs technical details locally, and shows a short actionable message.
- Never show a stack trace, SQL query or exception type to staff.