// =============================================
// File: InterestService.cs
// Description: Savings quarterly interest (min balance per quarter)
// Now includes: batch listing + safe reversal
// =============================================
using EvCharge.Api.Domain;
using EvCharge.Api.DTOs;
using EvCharge.Api.Repositories;

namespace EvCharge.Api.Services
{
    public class InterestService
    {
        private readonly AccountTypeRepository _types;
        private readonly AccountRepository _accounts;
        private readonly MemberRepository _members;
        private readonly TransactionRepository _txns;
        private readonly InterestBatchRepository _batches;

         private readonly bool _allowJoinQuarter;

        public InterestService(IConfiguration config)
        {
            _allowJoinQuarter = config.GetValue<bool>("Interest:AllowJoinQuarter");

            _types    = new AccountTypeRepository(config);
            _accounts = new AccountRepository(config);
            _members  = new MemberRepository(config);
            _txns     = new TransactionRepository(config);
            _batches  = new InterestBatchRepository(config);
        }

        // Fiscal: Jul→Sep (Q1), Oct→Dec (Q2), Jan→Mar (Q3), Apr→Jun (Q4)
        public (DateTime startUtc, DateTime endUtc) ResolveQuarter(string quarterKey)
        {
            var year = int.Parse(quarterKey[..4]);
            var q = int.Parse(quarterKey[5..]);

            DateTime start = q switch
            {
                1 => new DateTime(year, 7, 1, 0, 0, 0, DateTimeKind.Utc),
                2 => new DateTime(year, 10, 1, 0, 0, 0, DateTimeKind.Utc),
                3 => new DateTime(year + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                4 => new DateTime(year + 1, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                _ => throw new InvalidOperationException("Quarter must be one of Q1..Q4.")
            };

            DateTime end = q switch
            {
                1 => new DateTime(year, 9, 30, 23, 59, 59, DateTimeKind.Utc),
                2 => new DateTime(year, 12, 31, 23, 59, 59, DateTimeKind.Utc),
                3 => new DateTime(year + 1, 3, 31, 23, 59, 59, DateTimeKind.Utc),
                4 => new DateTime(year + 1, 6, 30, 23, 59, 59, DateTimeKind.Utc),
                _ => throw new InvalidOperationException("Quarter must be one of Q1..Q4.")
            };

            return (start, end);
        }

        private static bool QuarterHasEnded(DateTime quarterEndUtc) =>
            DateTime.UtcNow >= quarterEndUtc;

        private static void EnsureQuarterEndedOrThrow(string quarterKey, DateTime endUtc)
        {
            if (!QuarterHasEnded(endUtc))
                throw new InvalidOperationException($"Cannot process interest for {quarterKey}: the quarter has not ended yet.");
        }

        // public async Task<InterestPreviewResponse> PreviewAsync(InterestPreviewRequest req)
        // {
        //     var (start, end) = ResolveQuarter(req.Quarter);
        //     EnsureQuarterEndedOrThrow(req.Quarter, end);

        //     var type = await _types.GetByTypeIdAsync(req.TypeCode);
        //     if (type == null || !type.IsActive)
        //         throw new InvalidOperationException("Account type not found or inactive.");

        //     if (type.InterestMethod != InterestMethod.Quarterly_MinBalance)
        //         throw new InvalidOperationException("Selected type is not configured for quarterly interest.");

        //     if (type.InterestRateAnnual is null || type.InterestRateAnnual <= 0m)
        //         throw new InvalidOperationException("Annual interest rate is missing or invalid for this type.");

        //     var allMembers = await _members.GetAllAsync();
        //     var items = new List<InterestPreviewItem>();

        //     foreach (var member in allMembers.Where(m => m.Status == MemberStatus.Active))
        //     {
        //         var accs = await _accounts.GetByMemberAsync(member.MemberId);
        //         foreach (var acc in accs.Where(a => a.TypeCode == type.TypeId && a.Status == AccountStatus.Active))
        //         {
        //             var min = await ComputeMinBalanceInPeriodAsync(acc, start, end);
        //             if (min <= 0m) continue;

        //             var interest = Decimal.Round(min * (type.InterestRateAnnual!.Value / 4m), 2, MidpointRounding.AwayFromZero);
        //             if (interest <= 0m) continue;

        //             items.Add(new InterestPreviewItem
        //             {
        //                 MemberId = acc.MemberId,
        //                 TypeCode = acc.TypeCode,
        //                 MinBalanceInQuarter = min,
        //                 InterestAmount = interest
        //             });
        //         }
        //     }

        //     return new InterestPreviewResponse
        //     {
        //         TypeCode = req.TypeCode,
        //         Quarter = req.Quarter,
        //         PeriodStartUtc = start,
        //         PeriodEndUtc = end,
        //         Items = items
        //     };
        // }


public async Task<InterestPreviewResponse> PreviewAsync(InterestPreviewRequest req)
{
    var (start, end) = ResolveQuarter(req.Quarter);
    EnsureQuarterEndedOrThrow(req.Quarter, end);

    var type = await _types.GetByTypeIdAsync(req.TypeCode);
    if (type == null || !type.IsActive)
        throw new InvalidOperationException("Account type not found or inactive.");

    if (type.InterestMethod != InterestMethod.Quarterly_MinBalance)
        throw new InvalidOperationException("Selected type is not configured for quarterly interest.");

    if (type.InterestRateAnnual is null || type.InterestRateAnnual <= 0m)
        throw new InvalidOperationException("Annual interest rate is missing or invalid for this type.");

    var allMembers = await _members.GetAllAsync();
    var items = new List<InterestPreviewItem>();

    foreach (var member in allMembers.Where(m => m.Status == MemberStatus.Active))
    {
        var accs = await _accounts.GetByMemberAsync(member.MemberId);
        foreach (var acc in accs.Where(a => a.TypeCode == type.TypeId && a.Status == AccountStatus.Active))
        {
            // 🔒 Enforce policy: no interest if account opened mid-quarter
            if (!_allowJoinQuarter && acc.OpenedOnUtc > start)
            {
                continue; // skip this account for this quarter
            }

            var min = await ComputeMinBalanceInPeriodAsync(acc, start, end);
            if (min <= 0m) continue;

            var interest = Decimal.Round(min * (type.InterestRateAnnual!.Value / 4m), 2, MidpointRounding.AwayFromZero);
            if (interest <= 0m) continue;

            items.Add(new InterestPreviewItem
            {
                MemberId = acc.MemberId,
                TypeCode = acc.TypeCode,
                MinBalanceInQuarter = min,
                InterestAmount = interest
            });
        }
    }

    return new InterestPreviewResponse
    {
        TypeCode = req.TypeCode,
        Quarter = req.Quarter,
        PeriodStartUtc = start,
        PeriodEndUtc = end,
        Items = items
    };
}


        public async Task<InterestBatch> RunAsync(InterestRunRequest req, string userId)
        {
            var (start, end) = ResolveQuarter(req.Quarter);
            EnsureQuarterEndedOrThrow(req.Quarter, end);

            // Only block if there is an ACTIVE (non-reversed) batch.
// If the prior batch was reversed, we allow re-posting and will create a NEW batch.
var active = await _batches.GetActiveAsync(req.TypeCode, req.Quarter);
if (active != null)
    throw new InvalidOperationException($"Interest already posted for {req.TypeCode} in {req.Quarter} (batch {active.Id}). Reverse it first to re-post.");


            var preview = await PreviewAsync(new InterestPreviewRequest { TypeCode = req.TypeCode, Quarter = req.Quarter });

            var type = await _types.GetByTypeIdAsync(req.TypeCode);
            if (type == null) throw new InvalidOperationException("Account type not found.");

            foreach (var item in preview.Items)
            {
                var acc = await _accounts.GetAsync(item.MemberId, item.TypeCode);
                if (acc == null || acc.Status != AccountStatus.Active) continue;

                acc.PrincipalBalance = Decimal.Round(acc.PrincipalBalance + item.InterestAmount, 2, MidpointRounding.AwayFromZero);
                await _accounts.UpdateAsync(acc);

                var txn = new Transaction
                {
                    AccountId = acc.Id,
                    MemberId = acc.MemberId,
                    TypeCode = acc.TypeCode,
                    TxnType = TxnType.InterestCredit,
                    Amount = item.InterestAmount,
                    Narration = $"Quarterly interest {req.Quarter}",
                    EffectiveOnUtc = end,
                    PostedByUserId = userId,
                    BalanceAfterTxn = acc.PrincipalBalance
                };
                await _txns.CreateAsync(txn);

                await _types.SafeUpdateTotalsAsync(type.TypeId,
                    totalBalanceDelta: item.InterestAmount,
                    interestPaidDelta: item.InterestAmount);
            }

            var batch = new InterestBatch
            {
                TypeCode = req.TypeCode,
                QuarterKey = req.Quarter,
                PeriodStartUtc = start,
                PeriodEndUtc = end,
                PostedAtUtc = DateTime.UtcNow,
                TotalAccounts = preview.AccountCount,
                TotalInterest = preview.TotalInterest,
                PostedByUserId = userId
            };
            await _batches.CreateAsync(batch);
            return batch;
        }

        public Task<List<InterestBatch>> ListBatchesAsync(string? typeCode = null) =>
            _batches.ListAsync(typeCode);

        public Task<InterestBatch?> GetBatchAsync(string id) =>
            _batches.GetByIdAsync(id);

        public async Task ReverseAsync(InterestReverseRequest req, string userId)
        {
            // Only allow reversing a posted batch, not future/mid-quarter processing here.
            var (start, end) = ResolveQuarter(req.Quarter);

            var batch = await _batches.GetAsync(req.TypeCode, req.Quarter);
            if (batch == null)
                throw new InvalidOperationException("No posted batch found for this type and quarter.");
            if (batch.IsReversed)
                throw new InvalidOperationException("This batch is already reversed.");

            var type = await _types.GetByTypeIdAsync(req.TypeCode);
            if (type == null)
                throw new InvalidOperationException("Account type not found.");

            // Find all interest credits belonging to this batch
            var credits = await _txns.GetInterestCreditsForBatchAsync(req.TypeCode, req.Quarter, batch.PeriodEndUtc);

            // For each credit, subtract from account & write a reversal txn
            foreach (var credit in credits)
            {
                var acc = await _accounts.GetAsync(credit.MemberId, credit.TypeCode);
                if (acc == null) continue; // account may have been closed; skip hard fail, but in practice should exist

                acc.PrincipalBalance = Decimal.Round(acc.PrincipalBalance - credit.Amount, 2, MidpointRounding.AwayFromZero);
                await _accounts.UpdateAsync(acc);

                var reversal = new Transaction
                {
                    AccountId = acc.Id,
                    MemberId = acc.MemberId,
                    TypeCode = acc.TypeCode,
                    TxnType = TxnType.InterestReversal,
                    Amount = credit.Amount, // positive amount; semantic is "reversal of"
                    Narration = $"Reversal of quarterly interest {req.Quarter}",
                    EffectiveOnUtc = DateTime.UtcNow,
                    PostedByUserId = userId,
                    BalanceAfterTxn = acc.PrincipalBalance
                };
                await _txns.CreateAsync(reversal);

                await _types.SafeUpdateTotalsAsync(type.TypeId,
                    totalBalanceDelta: -credit.Amount,
                    interestPaidDelta: -credit.Amount);
            }

            await _batches.MarkReversedAsync(batch.Id, DateTime.UtcNow, userId);
        }

        // private async Task<decimal> ComputeMinBalanceInPeriodAsync(Account acc, DateTime start, DateTime end)
        // {
        //     var txnsBefore = await _txns.GetForAccountAsync(acc.Id, null, start.AddTicks(-1), null);
        //     decimal opening = 0m;
        //     if (txnsBefore.Count > 0)
        //     {
        //         opening = txnsBefore.Last().BalanceAfterTxn;
        //     }
        //     else
        //     {
        //         opening = (acc.OpenedOnUtc < start) ? acc.PrincipalBalance : 0m;
        //     }

        //     decimal minBal = opening;
        //     decimal running = opening;

        //     var txns = await _txns.GetForAccountAsync(acc.Id, start, end, null);
        //     foreach (var t in txns)
        //     {
        //         running = t.BalanceAfterTxn;
        //         if (running < minBal) minBal = running;
        //     }

        //     if (txns.Count == 0)
        //         minBal = Math.Min(opening, acc.PrincipalBalance);

        //     return Decimal.Round(minBal, 2, MidpointRounding.AwayFromZero);
        // }


        // Add this helper inside InterestService
private static decimal SignedAmount(TxnType type, decimal amount)
{
    // deposits & interest credits increase; withdrawals & reversals decrease
    return type switch
    {
        TxnType.Deposit => amount,
        TxnType.InterestCredit => amount,
        TxnType.Adjustment => amount,        // treat as positive; if you support negative adjustments, pass negative in txn.Amount
        TxnType.Withdrawal => -amount,
        TxnType.InterestReversal => -amount,
        _ => amount
    };
}

// REPLACE your existing ComputeMinBalanceInPeriodAsync with this version
private async Task<decimal> ComputeMinBalanceInPeriodAsync(Account acc, DateTime start, DateTime end)
{
    // Pull ALL txns up to the end of the quarter (so we can reconstruct running balance)
    // We DO NOT trust stored BalanceAfterTxn for historical reconstruction.
    var allUpToEnd = await _txns.GetForAccountAsync(acc.Id, null, end, null);
    if (allUpToEnd.Count == 0)
    {
        // No transactions at all up to end → effective balance is zero throughout
        return 0m;
    }

    // Sort strictly by EffectiveOnUtc, then by insertion order (id) to keep deterministic order
    // (GetForAccountAsync already sorts by date; we sort again defensively if needed)
    allUpToEnd = allUpToEnd
        .OrderBy(t => t.EffectiveOnUtc)
        .ToList();

    // Reconstruct the running balance from 0 using signed amounts
    decimal running = 0m;
    decimal openingAtStart = 0m;
    bool capturedOpening = false;

    decimal minInWindow = decimal.MaxValue;
    bool touchedWindow = false;

    foreach (var t in allUpToEnd)
    {
        // If this txn happens strictly before the window, we keep summing running
        if (t.EffectiveOnUtc < start)
        {
            running += SignedAmount(t.TxnType, t.Amount);
            continue;
        }

        // First time we enter the window, capture the opening balance at 'start'
        if (!capturedOpening)
        {
            openingAtStart = running;
            minInWindow = Math.Min(minInWindow, openingAtStart);
            capturedOpening = true;
            touchedWindow = true;
        }

        // If txn is within [start, end], apply and update min
        if (t.EffectiveOnUtc <= end)
        {
            running += SignedAmount(t.TxnType, t.Amount);
            minInWindow = Math.Min(minInWindow, running);
            continue;
        }

        // Past the window end → stop
        break;
    }

    if (!touchedWindow)
    {
        // No txns in window; openingAtStart is the balance at start (from pre-window reconstruction)
        // min is just that opening balance.
        minInWindow = openingAtStart;
    }

    // If account opened mid-quarter and you disallow join-quarter interest elsewhere,
    // it will be skipped before we ever get here.
    return Decimal.Round(Math.Max(minInWindow, 0m), 2, MidpointRounding.AwayFromZero);
}





    }
}
