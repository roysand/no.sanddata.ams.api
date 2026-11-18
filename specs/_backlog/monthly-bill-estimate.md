# Backlog: Estimated bill for the month

Status: the API side is built (spec `specs/010-monthly-bill-estimate`, PRs #27 to #30, merged 2026-11-18). What is
left is the frontend spec ("Expected bill" card) in `no.sanddata.ams.frontend`; see "For the frontend" below. Remove
this note once that spec is written. Originally written 2026-11-18 as input for `/speckit-specify`.

## For the frontend

- **Endpoint:** `GET /api/electricity-cost/bill?locationId={id}&month=yyyy-MM` (month optional; the current Oslo month
  when omitted). Full shape and field meanings:
  [`specs/010-monthly-bill-estimate/contracts/endpoints.md`](../010-monthly-bill-estimate/contracts/endpoints.md) and
  [`data-model.md`](../010-monthly-bill-estimate/data-model.md).
- **What to show:** `total.amount` (the expected bill), with `total.soFar` and `total.projected`; the two parts
  `strom` and `nettleie` with their `lines` in the order given, to mirror the invoice. Amounts include VAT and are not
  rounded: round to whole kroner (or øre) in the UI.
- **Estimate or final:** `status` is `Estimate` for the current month and `Final` after it ends. For an estimate,
  `projection.basedOnDays` (0 to 7) says how much data the projection rests on; early in a new location's life it is
  low and the card should say the figure is uncertain. `capacity.provisional` is true until the month ends: the step
  can still rise.
- **Not set, never zero:** a line with `set: false` (monthly fee, grid tariff or capacity steps missing) has null
  amounts and is left out of the totals; `isComplete: false` then says the total is too low. Show "not set" or a hint,
  never 0.
- **Norgespris credit:** the `NorgesPrisCredit` line is negative (it lowers the bill) and has 0 % VAT, as on the
  invoice; it can be positive in a month where spot was below the Norgespris rate.
- **Changed elsewhere by this feature:** every hourly, daily, current-hour and day-price figure of a Norgespris
  location with a supplier markup rose by markup × 1.25 per kWh (the påslag is now charged under Norgespris), and
  the Norgespris saving no longer contains the markup. On the Admin pricing page, `supplierMonthlyFee` is new
  (NOK per month excluding VAT, optional, an omitted value keeps the current fee) and `isComplete` now needs both a
  grid tariff and the fee. The markup has at most 4 decimals: the July invoice's 3.99 øre incl. VAT is entered as
  0.0319.

## What the user wants

A location's expected invoice for the current month, as it will look when it arrives: what has been used so far,
what the rest of the month will probably add, and the fixed amounts. It should match the real invoice closely enough
that Roy can check one against the other. Other electricity apps show this prominently; the app shows today only the
cost per hour and day, without the monthly fee and the capacity charge.

## Ground truth: Roy's invoice for July 2026

One combined invoice from the supplier (Fjordkraft) with the grid part from the grid owner (Elvia). The location is a
**holiday home on Norgespris**. All amounts include 25 % VAT unless noted.

**Strøm (supplier), 417,33 kr**

| Line | Quantity | Price | VAT | Amount |
|---|---|---|---|---|
| Månedlig fastbeløp | 31 days | 34,00 kr/month | 25 % | 34,00 |
| Din strømpris | 240,31 kWh | 155,52 øre/kWh | 25 % | 373,74 |
| Påslag | 240,31 kWh | 3,99 øre/kWh | 25 % | 9,59 |

**Nettleie (grid), 256,22 kr**

| Line | Quantity | Price | VAT | Amount |
|---|---|---|---|---|
| Energiledd weekdays 06–22 | 95,58 kWh | 36,24 øre/kWh | 25 % | 34,64 |
| Energiledd nights and weekends | 144,73 kWh | 21,24 øre/kWh | 25 % | 30,74 |
| Norgespris for fritidsbolig | −240,31 kWh | 1,06 kr/kWh | **0 %** | −253,58 |
| Fastledd 5–10 kW | 31 days | 420,00 kr/month | 25 % | 420,00 |
| Elavgift | 95,58 + 144,73 kWh | 8,91 øre/kWh | 25 % | 8,51 + 12,90 |
| Energifondet | 95,58 + 144,73 kWh | 1,25 øre/kWh | 25 % | 1,20 + 1,81 |

**Total 673,55 kr** (488,12 excluding VAT + 185,43 VAT, where the 0 % VAT credit is −253,58).

### What the invoice tells us

1. **The supplier bills the full spot price, even on Norgespris.** The grid owner then credits the difference per kWh
   without VAT. The credit is (spot − Norgespris) × 1,25: (1,2442 − 0,40) × 1,25 = 1,0552 kr/kWh, shown rounded as
   1,06. Net energy is 1,5552 − 1,0552 = 0,50 kr/kWh, the same as the 0,40 kr Norgespris rate with VAT. The backend
   prices Norgespris hours directly instead; the net should come out the same, but the spec should prove that to the
   øre.
2. **Påslag is charged on Norgespris too.** `CostCalculator.TermsAsync` sets the markup to 0 under Norgespris ("No
   supplier markup under Norgespris"). That is wrong for this supplier: today's hourly price and cost for Norgespris
   locations are too low by the markup (9,59 kr in July). Fixing it changes existing numbers (hourly and daily cost,
   the current hour, the Norgespris saving), so the spec must say so. It could also be a separate small fix first.
3. **The supplier's monthly fee is not stored anywhere.** It belongs to the supplier contract, like
   `Location.SupplierMarkupPerKwh`, so store it per location next to the markup: NOK per month excluding VAT (34,00 kr
   = 27,20 excluding VAT), with VAT added unless the location is VAT-exempt. Set it on the existing admin Location
   pricing page (`PUT /api/admin/location-pricing/{locationId}`).
4. **The monthly fixed amounts are not prorated.** Both the supplier fee and the Fastledd are a full month for 31 days.
5. **The Fastledd is the capacity charge,** which `GET /api/electricity-cost/capacity` already gives (step and
   amount from the three daily peaks).
6. **The invoice has two parts: Strøm and Nettleie.** The estimate should return them separately, so the card can
   mirror the invoice.

## Acceptance test

Compute the **finished month July 2026** for Roy's holiday home with the stored readings and prices. The total should
be **673,55 kr**, with the Strøm and Nettleie parts as above. Agree a tolerance in the spec, since the invoice rounds
every line. This also checks the påslag fix and the Norgespris credit. It needs the location's July readings and
July's prices and rates to be in the database; confirm that before relying on it.

## Open questions for `/speckit-clarify`

- **Projection for the rest of the month:** the average per day so far, the same days last week, last year's month,
  or a mix? Early in the month there is little to go on; how should uncertainty be shown (a range, or a note)?
- **Projected price:** remaining hours have no known spot price beyond tomorrow. Use the month's average so far, a
  recent average, or prices where known plus an average after that?
- **Capacity charge:** the step can still rise until the month ends. Use the step so far, or project the peaks?
- **Norgespris cap:** a holiday home has its own monthly cap; kWh above it are priced on spot terms. The projection
  must count the cap when it projects kWh.
- **Endpoint shape:** one endpoint for a month (`GET /api/electricity-cost/bill?locationId=&month=yyyy-MM`) that also
  works for finished months (needed for the acceptance test and for checking past invoices)?
- **Line items:** return the lines as on the invoice (fee, energy, markup, grid energy day and night, Norgespris
  credit, capacity charge, taxes) or only the two subtotals?
- **Who may see it:** same access rules as the other cost endpoints for the location?

## Out of scope (for now)

- Different suppliers' invoice layouts. Fjordkraft and Elvia are the reference.
- Payment, due dates and invoice history from the supplier.
