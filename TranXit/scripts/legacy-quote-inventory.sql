/*
  Read-only launch inventory. Run against CourierJobService with a SELECT-only principal.
  Emits identifiers/amounts/ETA facts, not customer or courier personal data. No repair is implied.
  Existing awarded prices remain authoritative; this does not recalculate or reprice them.
  A NULL accepted ETA means unresolved/missing data, never a guessed delivery date.
  Snapshot this report during a quiet window for operational review; it is not a mutation gate.
*/
WITH JobFacts AS (
    SELECT j.Id, j.IsJobStatusFromBid, j.AcceptedBidProposalId,
        ap.BiddingId AS AcceptedBidId, ap.DeliveryDateUtc AS AcceptedDeliveryDateUtc,
        ab.JobId AS AcceptedBidJobId, ab.JobStatusId AS AcceptedBidStatusId,
        (SELECT COUNT(*) FROM Biddings wb WHERE wb.JobId = j.Id AND wb.JobStatusId IN (3, 6, 7)) AS WinnerCount
    FROM Jobs j
    LEFT JOIN BiddingProposals ap ON ap.Id = j.AcceptedBidProposalId
    LEFT JOIN Biddings ab ON ab.Id = ap.BiddingId
), ProposalFacts AS (
    SELECT p.BiddingId, p.IsBaseBid, p.Total, p.DeliveryDateUtc,
        CASE WHEN TRY_CONVERT(decimal(18,2), p.Total) <>
            COALESCE(TRY_CONVERT(decimal(18,2), b.PickupCharges), 0) +
            COALESCE(TRY_CONVERT(decimal(18,2), b.HandlingCharges), 0) +
            COALESCE(TRY_CONVERT(decimal(18,2), b.CustomClearanceCharges), 0) +
            COALESCE(items.ItemTotal, 0) + COALESCE(charges.ChargeTotal, 0) THEN 1 ELSE 0 END AS ComponentMismatch
    FROM BiddingProposals p
    INNER JOIN Biddings b ON b.Id = p.BiddingId
    OUTER APPLY (SELECT SUM(TRY_CONVERT(decimal(18,2), pi.ItemTotal)) AS ItemTotal
        FROM BiddingProposalItems pi WHERE pi.BiddingProposalId = p.Id) items
    OUTER APPLY (SELECT SUM(TRY_CONVERT(decimal(18,2), c.Amount)) AS ChargeTotal
        FROM BiddingCharges c WHERE c.BiddingId = b.Id) charges
), QuoteFacts AS (
    SELECT j.*, b.Id AS BidId, b.JobStatusId AS BidStatusId, b.TotalAmount,
        bp.BaseCount, bp.BaseTotal, bp.EarliestProposalEtaUtc, bp.LatestProposalEtaUtc,
        bp.InvalidProposalCount, bp.MissingEtaCount, bp.ComponentMismatchCount,
        (SELECT COUNT(*) FROM BiddingProposalItems pi
         INNER JOIN BiddingProposals p ON p.Id = pi.BiddingProposalId
         LEFT JOIN JobItems ji ON ji.Id = pi.JobItemId
         WHERE p.BiddingId = b.Id AND (ji.Id IS NULL OR ji.JobId <> b.JobId OR ji.Quantity IS NULL OR ji.Quantity <= 0
             OR pi.UnitPrice IS NULL OR pi.ItemTotal IS NULL
             OR TRY_CONVERT(decimal(18,2), pi.UnitPrice) * ji.Quantity <> TRY_CONVERT(decimal(18,2), pi.ItemTotal))) AS ItemMismatchCount
    FROM JobFacts j
    LEFT JOIN Biddings b ON b.JobId = j.Id
    OUTER APPLY (
        SELECT COUNT(CASE WHEN p.IsBaseBid = 1 THEN 1 END) AS BaseCount,
            MAX(CASE WHEN p.IsBaseBid = 1 THEN p.Total END) AS BaseTotal,
            MIN(p.DeliveryDateUtc) AS EarliestProposalEtaUtc, MAX(p.DeliveryDateUtc) AS LatestProposalEtaUtc,
            COALESCE(SUM(CASE WHEN p.Total IS NULL OR p.Total < 0 OR p.Total > 9999999999999.99
                OR TRY_CONVERT(decimal(18,2), p.Total) IS NULL
                OR CONVERT(float, TRY_CONVERT(decimal(18,2), p.Total)) <> p.Total THEN 1 ELSE 0 END), 0) AS InvalidProposalCount,
            COUNT(CASE WHEN p.DeliveryDateUtc IS NULL THEN 1 END) AS MissingEtaCount,
            COALESCE(SUM(p.ComponentMismatch), 0) AS ComponentMismatchCount
        FROM ProposalFacts p
        WHERE p.BiddingId = b.Id
    ) bp
), Inventory AS (
    SELECT q.Id AS JobId, q.BidId, issues.Category, q.BidStatusId,
        q.TotalAmount AS StoredBidTotal, q.BaseTotal AS StoredBaseProposalTotal,
        q.AcceptedBidProposalId, q.AcceptedBidId,
        CASE WHEN q.IsJobStatusFromBid = 1 AND q.AcceptedBidJobId = q.Id AND q.AcceptedBidStatusId IN (3, 6, 7) AND q.WinnerCount = 1
            THEN q.AcceptedDeliveryDateUtc END AS ResolvedAcceptedEtaUtc,
        q.EarliestProposalEtaUtc, q.LatestProposalEtaUtc
    FROM QuoteFacts q
    CROSS APPLY (VALUES
        ('unawarded_base_count', CASE WHEN q.BidId IS NOT NULL AND ISNULL(q.IsJobStatusFromBid, 0) = 0 AND q.AcceptedBidProposalId IS NULL AND q.WinnerCount = 0 AND q.BaseCount <> 1 THEN 1 ELSE 0 END),
        ('unawarded_stored_total_invalid', CASE WHEN q.BidId IS NOT NULL AND ISNULL(q.IsJobStatusFromBid, 0) = 0 AND q.AcceptedBidProposalId IS NULL AND q.WinnerCount = 0 AND
            (q.TotalAmount < 0 OR q.TotalAmount > 9999999999999.99 OR TRY_CONVERT(decimal(18,2), q.TotalAmount) IS NULL OR CONVERT(float, TRY_CONVERT(decimal(18,2), q.TotalAmount)) <> q.TotalAmount) THEN 1 ELSE 0 END),
        ('unawarded_base_total_mismatch', CASE WHEN ISNULL(q.IsJobStatusFromBid, 0) = 0 AND q.AcceptedBidProposalId IS NULL AND q.WinnerCount = 0 AND q.BaseCount = 1 AND q.TotalAmount <> q.BaseTotal THEN 1 ELSE 0 END),
        ('unawarded_proposal_total_invalid', CASE WHEN ISNULL(q.IsJobStatusFromBid, 0) = 0 AND q.AcceptedBidProposalId IS NULL AND q.WinnerCount = 0 AND q.InvalidProposalCount > 0 THEN 1 ELSE 0 END),
        ('unawarded_components_mismatch', CASE WHEN ISNULL(q.IsJobStatusFromBid, 0) = 0 AND q.AcceptedBidProposalId IS NULL AND q.WinnerCount = 0 AND q.ComponentMismatchCount > 0 THEN 1 ELSE 0 END),
        ('unawarded_item_quantity_mismatch', CASE WHEN ISNULL(q.IsJobStatusFromBid, 0) = 0 AND q.AcceptedBidProposalId IS NULL AND q.WinnerCount = 0 AND q.ItemMismatchCount > 0 THEN 1 ELSE 0 END),
        ('unawarded_proposal_eta_missing', CASE WHEN ISNULL(q.IsJobStatusFromBid, 0) = 0 AND q.AcceptedBidProposalId IS NULL AND q.WinnerCount = 0 AND q.MissingEtaCount > 0 THEN 1 ELSE 0 END),
        ('unresolved_award_reference', CASE WHEN q.IsJobStatusFromBid = 1 AND (q.AcceptedBidProposalId IS NULL OR q.AcceptedBidId IS NULL OR q.AcceptedBidJobId <> q.Id OR q.AcceptedBidStatusId IS NULL OR q.AcceptedBidStatusId NOT IN (3, 6, 7)) THEN 1 ELSE 0 END),
        ('award_winner_count', CASE WHEN (q.IsJobStatusFromBid = 1 OR q.AcceptedBidProposalId IS NOT NULL) AND q.WinnerCount <> 1 THEN 1 ELSE 0 END),
        ('award_flag_mismatch', CASE WHEN ISNULL(q.IsJobStatusFromBid, 0) = 0 AND (q.AcceptedBidProposalId IS NOT NULL OR q.WinnerCount > 0) THEN 1 ELSE 0 END),
        ('accepted_eta_missing', CASE WHEN q.IsJobStatusFromBid = 1 AND q.AcceptedBidId IS NOT NULL AND q.AcceptedDeliveryDateUtc IS NULL THEN 1 ELSE 0 END)
    ) issues(Category, IsIssue)
    WHERE issues.IsIssue = 1
)
SELECT * FROM Inventory ORDER BY JobId, BidId, Category;
