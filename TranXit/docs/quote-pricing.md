# Quote Amount Contract

For each proposal:

`total = sum(item totals) + pickup + handling + clearance + sum(custom charges)`

Shared charges apply once to each alternative, not once per item. Exactly one
proposal is the base. Its calculated total is the bid's initial displayed amount.
Item total must equal unit price times the persisted shipment-item quantity.
Empty item lists are valid for lump-sum freight quotes.

New amounts are nonnegative JSON numbers in plain decimal notation, with at most
two decimal places. Zero is valid; absence is null. Mismatched totals, malformed
numbers, duplicate base proposals, duplicate item rows and overflow are rejected
without writes. Calculations use decimal before the existing SQL float boundary.
The technical maximum is 9,999,999,999,999.99, keeping cents within 15 significant
decimal digits and JavaScript's safe integer range. This is not a commercial limit.

Acceptance uses the selected stored all-in proposal amount, never adds charges a
second time, and retains every proposal. Historical rows are not repriced or
backfilled. First-time acceptance of a legacy bid whose displayed total differs
from its base proposal is rejected for explicit review. Identical retries of an
already accepted proposal do not modify price, history or lifecycle status.

This correction does not define currency conversion, tax, payment or refund rules.
A decimal SQL schema migration remains separate work before payment integration.
