# Legacy quote and award inventory

`legacy-quote-inventory.sql` is a read-only review aid for the CourierJob database. It does not
reprice, cancel, delete, select a winning proposal, or correct delivery dates. Do not run it with
an application writer or administrator account when a SELECT-only account is available.

Before an operational run, confirm the intended database and approved read-only credentials.
Execute the SQL file using the existing SQL client/secret-handling procedure and save the result
outside the repository with environment, UTC capture time, and release SHA. No live environment
is implied by the disposable SQL Server integration tests.

The output contains identifiers and stored monetary/date facts, not names, addresses, or contacts.
There may be multiple rows per bid, and job-level findings repeat for each bid. An unresolved job
with no bids has a null BidId. Review by JobId before counting affected jobs.

Categories cover unawarded base-proposal cardinality, stored amount/cents validity, base-total and
component mismatches, item quantities, missing proposal ETA, unresolved awarded proposal references,
winner count, inconsistent award flags, and missing accepted ETA. Proposed earliest/latest dates
are historical bounds, not accepted ETAs. `ResolvedAcceptedEtaUtc` stays null unless the accepted
proposal belongs to the job's sole winning bid and the award flag is set.

This is a diagnostic snapshot, not a complete financial audit or concurrency-safe mutation input.
Use a quiet review window; rerun and independently verify any selected records before a separately
approved correction. A clean report does not prove currency, payment, retention, or legal compliance.
Existing awarded prices intentionally remain authoritative even if a legacy proposal differs.

Verification: `LegacyQuoteInventoryTests` runs the exact script in disposable real SQL Server under
a principal denied INSERT/UPDATE/DELETE/ALTER/EXECUTE, compares all job/quote rows before and after,
and checks unresolved ETA, legacy price preservation, and mismatch categories.
