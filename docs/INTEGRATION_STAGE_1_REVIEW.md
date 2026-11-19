# ORVA Stage 1 Integration Patch — Review Notes

This is an integration overlay for the current ORVA baseline. It is not a complete release or a verified Windows build.

## Included

- Register Gemini's `IBillingService` implementation in `AppServices`.
- Construct `BillingService` with the existing local SQLite database and `StockSnapshotWriter`.
- Embed migration `002_BillingTaxConfig.sql` in the WPF project.
- Open the application directly on New Billing after first-run shop setup.
- Connect BillingView to the service through constructor injection.
- Map the existing ShopProfile to the paper preview shop header.
- Add WPF support classes referenced by Qwen's billing screen and paper preview.
- Preserve C# 7.3 / .NET Framework 4.8.1 compatibility.

## Important source files to bring in from the existing Dropbox batches

Copy the reviewed files from these workspace locations into the paths stated inside each batch:

- `02-GEMINI-BACKEND/ORVA.zip`: the Billing namespace, BillingRepository, BillingService, calculator, FEFO allocator, migration 002 and tests.
- `03-QWEN-WPF-UI/qwen batch 2r`: BillingViewModel.cs.
- `03-QWEN-WPF-UI/qwen batch 3`: BillingView.xaml and BillingView.xaml.cs.
- `03-QWEN-WPF-UI/qwen batch 4`: invoice preview control, preview window and English/Bengali resource additions.

Do not use the superseded Qwen Batch 2.

## Required follow-up fixes before the first Windows build

1. **Busy-state edit lock.** In BillingViewModel, NewBillCommand currently has no busy-state CanExecute condition, and ClearDraft currently only checks shutdown. Add `_isBusy` guards. Set `CanEdit=false` on cart/tender row view models while `IsBusy=true`, and restore it when `IsBusy=false`. Search, customer, cart quantity and tender inputs must not be writable during invoice finalisation. The invoice must be finalised from the exact draft snapshot created before the service call.
2. **Clear Bill click handler.** Add an immediate `if (ViewModel.IsBusy) return;` guard to `ClearBill_Click`.
3. **Search result keyboard routing.** Restrict Delete to the cart when focus is inside the cart, not when focus is in search results or other controls.
4. **PrintRequested event.** Subscribe only after a proper physical print service has been implemented. Do not show a successful print state until the host printer confirms the job was submitted.
5. **Mixed-batch page preview.** On A4/A5, show the unit price for every batch allocation. The current allocation template has batch, expiry and quantity but omits each allocation's rate.
6. **Signature scallop.** Qwen's current dashed/dotted line is not the agreed physical scallop-cutout edge. Replace it with a true repeated semicircle geometry and vermilion underlay when refining the preview.
7. **Typography.** Bundle and apply the agreed Noto Serif / Noto Sans / Noto Serif Bengali / Noto Sans Bengali / IBM Plex Mono font families. Do not rely only on system-installed fonts.
8. **Tax configuration UI.** Gemini's service correctly blocks billing where shop tax settings or per-medicine GST configuration are missing. This patch does not invent tax rates or tax treatment. Add a separate, explicit Settings workflow and validate it with the shop's CA before release.
9. **Snapshot completeness.** Extend StockData.json in a new compatible format version to include GST-configured state and existing barcode mappings if they are part of the portable medicine catalogue. Keep import history preserving.
10. **Schema migration safety.** Verify migration 002 can be applied once and rolled back/kept atomic with the existing migration runner. Do not modify migration 001.

## Verification status

The current environment does not contain Visual Studio/MSBuild or the .NET Framework 4.8.1 targeting pack. No Windows compilation, runtime, print, scanner, DPI, Bengali glyph or installer test has been claimed. The source files must be built and exercised on Windows 10 before this stage can be considered verified.


## Finalisation safety patch

Run `apply-billing-safety-fixes.ps1` after copying the Gemini and Qwen source files into the ORVA project. It makes guarded, exact text replacements and stops rather than guessing if the source differs. It blocks edits to the draft while `FinalizeInvoice` is running, prevents New Bill/Clear Bill from clearing a draft mid-save, and restricts the Delete shortcut to the cart.

The script is intentionally separate from the basic overlay so source differences cannot cause silent or broad replacements.


The current safety script uses line-ending-tolerant matches and stops on unexpected source differences. It is safe to run again; already-applied changes are detected.