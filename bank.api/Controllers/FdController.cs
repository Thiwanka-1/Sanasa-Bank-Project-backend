// =============================================
// File: FdController.cs
// Description: Fixed Deposit flows (open, premature close, mature/renew)
// Notes:
//  - Uses Account.Attributes["fd"] snapshot to lock tenor/rates for this FD
//  - Premature: no interest if < threshold months; else prematureAnnualRate
//  - Mature: Withdraw to savings OR Renew with new tenor and chosen renewal mode
// =============================================
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MongoDB.Bson;
using EvCharge.Api.Domain;
using EvCharge.Api.DTOs;
using EvCharge.Api.Repositories;
using System.Globalization;
using System.Linq;

namespace EvCharge.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class FdController : ControllerBase
    {
        private readonly MemberRepository _members;
        private readonly AccountTypeRepository _types;
        private readonly AccountRepository _accounts;
        private readonly TransactionRepository _txns;

        public FdController(IConfiguration config)
        {
            _members  = new MemberRepository(config);
            _types    = new AccountTypeRepository(config);
            _accounts = new AccountRepository(config);
            _txns     = new TransactionRepository(config);
        }

        private static decimal Round2(decimal v) => decimal.Round(v, 2, MidpointRounding.AwayFromZero);

        private static (int tenorDays, decimal annualRate, int thresholdMonths, decimal prematureAnnualRate, decimal openPrincipal)
            ReadSnapshot(BsonDocument snap)
        {
            var tenorDays = GetInt(snap, "tenorDays", 0);
            var annualRate = GetDecimal(snap, "annualRate", 0m);
            var thresholdMonths = GetInt(snap, "prematureThresholdMonths", 3);
            var prematureAnnual = GetDecimal(snap, "prematureAnnualRate", 0.03m);
            var openPrincipal = GetDecimal(snap, "openPrincipal", 0m);
            return (tenorDays, annualRate, thresholdMonths, prematureAnnual, openPrincipal);
        }

        private static decimal ComputeFdMaturityInterest(decimal principal, int tenorDays, decimal annualRate)
        {
            if (principal <= 0 || tenorDays <= 0 || annualRate <= 0) return 0m;
            var interest = principal * annualRate * (tenorDays / 365m);
            return Round2(interest);
        }

        private static decimal ComputeFdPrematureInterest(
    decimal principal,
    DateTime openedOnUtc,
    DateTime asOfUtc,
    int thresholdMonths,
    decimal prematureAnnualRate)
{
    if (principal <= 0 || prematureAnnualRate <= 0m) return 0m;

    // whole days only
    var elapsedWholeDays = (int)Math.Floor((asOfUtc - openedOnUtc).TotalDays);
    if (elapsedWholeDays < 0) elapsedWholeDays = 0;

    var thresholdDays = thresholdMonths * 30; // policy: 30-day months
    if (elapsedWholeDays < thresholdDays) return 0m;

    var interest = principal * prematureAnnualRate * ((decimal)elapsedWholeDays / 365m);
    return Round2(interest);
}



        // ------------------- FD OPEN -------------------

        // POST /api/fd/open
        [HttpPost("open")]
        public async Task<IActionResult> Open([FromBody] OpenFixedDepositRequest req)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            var member = await GetActiveMemberOrThrow(req.MemberId);
            var fdType = await GetTypeOrThrow(req.TypeCode);

            if (member.Type != PartyType.Member)
                return BadRequest(new { message = "Only members can open FD products." });

            if (fdType.Category != AccountCategory.MemberDeposits)
                return BadRequest(new { message = "FD Type must be in MemberDeposits category." });

            // Enforce one FD account per (MemberId, TypeCode) as per your current unique index
            var existing = await _accounts.GetAsync(req.MemberId, req.TypeCode);
            if (existing != null)
                return Conflict(new { message = "FD already exists for this member and type code." });

            // Resolve tenor and rate from the product's rate table
var safeAttrs = await EnsureFdProductAttributes(fdType);
var (tenorDays, annualRate) = ResolveFdRate(safeAttrs, req.TenorDays);

            // Create FD account with snapshot of terms
            var fd = new Account
            {
                MemberId = req.MemberId,
                TypeCode = req.TypeCode,
                Category = fdType.Category,
                Status = AccountStatus.Active,
                PrincipalBalance = 0m, // will deposit immediately
                AccruedInterest = 0m,
                OpenedOnUtc = DateTime.UtcNow,
                MaturityOnUtc = DateTime.UtcNow.AddDays(tenorDays),
                LinkedSavingsAccountNumber = req.MemberId, // same logical number; savings uses TypeCode to select product
                Attributes = new BsonDocument
                {
                    ["fd"] = new BsonDocument
                    {
                        ["tenorDays"] = tenorDays,
                        ["annualRate"] = annualRate,
                        // snapshot default premature policy (can be overridden in product Attributes)
                        ["prematureThresholdMonths"] = GetInt(fdType.Attributes, "fdPrematureThresholdMonths", 3),
                        ["prematureAnnualRate"] = GetDecimal(fdType.Attributes, "fdPrematureAnnualRate", 0.03m),
                        ["openPrincipal"] = req.Principal
                    }
                }
            };

            await _accounts.CreateAsync(fd);

            // Move principal into FD (as a deposit)
            await Credit(fd, fdType, req.Principal, "FD open principal deposit");

            // Ensure the target savings account exists for payouts (not moving money now, just validating)
            await GetSavingsDestinationOrThrow(req.MemberId, req.SavingsTypeCode);

            return CreatedAtAction(nameof(Get), new { memberId = req.MemberId, typeCode = req.TypeCode }, new
            {
                message = "FD opened.",
                memberId = req.MemberId,
                typeCode = req.TypeCode,
                principal = req.Principal,
                tenorDays,
                annualRate,
                maturityOnUtc = fd.MaturityOnUtc
            });
        }

        // GET /api/fd/{memberId}/{typeCode}
        [HttpGet("{memberId}/{typeCode}")]
        public async Task<IActionResult> Get(string memberId, string typeCode)
        {
            var acc = await _accounts.GetAsync(memberId, typeCode);
            if (acc == null) return NotFound();
            return Ok(MapFd(acc));
        }


        // ------------------- FD PREMATURE CLOSE -------------------

        // POST /api/fd/close/premature
        [HttpPost("close/premature")]
        public async Task<IActionResult> PrematureClose([FromBody] PrematureCloseFixedDepositRequest req)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            var member = await GetActiveMemberOrThrow(req.MemberId);
            var fdType = await GetTypeOrThrow(req.TypeCode);

            var fd = await _accounts.GetAsync(req.MemberId, req.TypeCode);
            if (fd == null) return NotFound(new { message = "FD account not found." });
            if (fd.Status != AccountStatus.Active) return BadRequest(new { message = "FD is not active." });

            // Snapshot terms
            var snap = await GetFdSnapshotOrRebuildAsync(fd);
            var tenorDays = GetInt(snap, "tenorDays", 0);
            var annualRate = GetDecimal(snap, "annualRate", 0m);
            var prematureThresholdMonths = GetInt(snap, "prematureThresholdMonths", 3);
            var prematureAnnualRate = GetDecimal(snap, "prematureAnnualRate", 0.03m);

            var principal = fd.PrincipalBalance;
            var opened = fd.OpenedOnUtc;
            var elapsedDays = (DateTime.UtcNow - opened).TotalDays;

            // Policy: no interest if elapsed < threshold months
            var thresholdDays = prematureThresholdMonths * 30; // simple month=30 rule for policy
            decimal interest = 0m;

            if (elapsedDays >= thresholdDays)
            {
                // Premature interest using prematureAnnualRate prorated by elapsed days
                interest = decimal.Round(principal * prematureAnnualRate * ((decimal)elapsedDays / 365m), 2, MidpointRounding.AwayFromZero);
            }
            else
            {
                interest = 0m;
            }

            // Payout to savings
            var savings = await GetSavingsDestinationOrThrow(req.MemberId, req.SavingsTypeCode);
            var savingsType = await _types.GetByTypeIdAsync(savings.TypeCode) ?? throw new InvalidOperationException("Savings type missing.");

            // 1) (Optional) write interest credit for audit, if any
            if (interest > 0m)
            {
                fd.PrincipalBalance = decimal.Round(fd.PrincipalBalance + interest, 2, MidpointRounding.AwayFromZero);
                await _accounts.UpdateAsync(fd);
                await _txns.CreateAsync(new Transaction
                {
                    AccountId = fd.Id,
                    MemberId = fd.MemberId,
                    TypeCode = fd.TypeCode,
                    TxnType = TxnType.InterestCredit,
                    Amount = interest,
                    Narration = $"FD premature interest ({elapsedDays:F0}d at {prematureAnnualRate:P})",
                    EffectiveOnUtc = DateTime.UtcNow,
                    PostedByUserId = User?.Identity?.Name ?? "system",
                    BalanceAfterTxn = fd.PrincipalBalance
                });
                await _types.SafeUpdateTotalsAsync(fdType.TypeId, interest, interest);
            }

            // 2) move principal (and interest, if credited) out of FD to savings
            var payout = principal + interest;

            await Debit(fd, fdType, principal, "FD premature principal payout");
            if (interest > 0m)
                await Debit(fd, fdType, interest, "FD premature interest payout");

            await Credit(savings, savingsType, payout, "FD premature payout");

            // 3) close FD
            await _accounts.SetStatusAsync(req.MemberId, req.TypeCode, AccountStatus.Closed);

            return Ok(new { message = "FD prematurely closed.", elapsedDays = (int)elapsedDays, interest, payout });
        }

        // ------------------- FD MATURE OR RENEW -------------------

        // POST /api/fd/mature-or-renew
        [HttpPost("mature-or-renew")]
        public async Task<IActionResult> MatureOrRenew([FromBody] MatureOrRenewFixedDepositRequest req)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            var member = await GetActiveMemberOrThrow(req.MemberId);
            var fdType = await GetTypeOrThrow(req.TypeCode);

            var fd = await _accounts.GetAsync(req.MemberId, req.TypeCode);
            if (fd == null) return NotFound(new { message = "FD account not found." });
            if (fd.Status != AccountStatus.Active) return BadRequest(new { message = "FD is not active." });
            if (fd.MaturityOnUtc is null) return BadRequest(new { message = "FD maturity date is not set." });
            if (DateTime.UtcNow < fd.MaturityOnUtc.Value)
                return BadRequest(new { message = "FD has not yet matured. Use premature close if required." });

            var snap = await GetFdSnapshotOrRebuildAsync(fd);
            var tenorDays = GetInt(snap, "tenorDays", 0);
            var annualRate = GetDecimal(snap, "annualRate", 0m);
            var principal  = fd.PrincipalBalance;

            if (tenorDays <= 0 || annualRate <= 0m)
                return BadRequest(new { message = "FD snapshot tenor/rate is invalid." });

            // maturity interest for full term
            var interest = decimal.Round(principal * annualRate * (tenorDays / 365m), 2, MidpointRounding.AwayFromZero);

            // destination savings
            var savings = await GetSavingsDestinationOrThrow(req.MemberId, req.SavingsTypeCode);
            var savingsType = await _types.GetByTypeIdAsync(savings.TypeCode) ?? throw new InvalidOperationException("Savings type missing.");

            var action = (req.Action ?? "withdraw").Trim().ToLowerInvariant();

            if (action == "withdraw")
            {
                // Pay principal + interest to savings
                // 1) credit interest to FD (audit)
                fd.PrincipalBalance = decimal.Round(fd.PrincipalBalance + interest, 2, MidpointRounding.AwayFromZero);
                await _accounts.UpdateAsync(fd);
                await _txns.CreateAsync(new Transaction
                {
                    AccountId = fd.Id,
                    MemberId = fd.MemberId,
                    TypeCode = fd.TypeCode,
                    TxnType = TxnType.InterestCredit,
                    Amount = interest,
                    Narration = $"FD maturity interest ({tenorDays}d)",
                    EffectiveOnUtc = DateTime.UtcNow,
                    PostedByUserId = User?.Identity?.Name ?? "system",
                    BalanceAfterTxn = fd.PrincipalBalance
                });
                await _types.SafeUpdateTotalsAsync(fdType.TypeId, interest, interest);

                // 2) move principal + interest out of FD
                await Debit(fd, fdType, principal, "FD maturity principal payout");
                await Debit(fd, fdType, interest, "FD maturity interest payout");

                // 3) credit savings
                var payout = principal + interest;
                await Credit(savings, savingsType, payout, $"FD maturity payout from {req.TypeCode}");

                // 4) close FD
                await _accounts.SetStatusAsync(req.MemberId, req.TypeCode, AccountStatus.Closed);

                return Ok(new { message = "FD matured and withdrawn to savings.", principal, interest, payout });
            }
            else if (action == "renew")
            {
                if (req.TenorDays is null || req.TenorDays <= 0)
                    return BadRequest(new { message = "For renewal, TenorDays is required and must be > 0." });
                if (req.RenewalMode is null)
                    return BadRequest(new { message = "For renewal, RenewalMode is required (PrincipalOnly | PrincipalPlusInterest)." });

                // 1) credit interest to FD (audit)
                fd.PrincipalBalance = decimal.Round(fd.PrincipalBalance + interest, 2, MidpointRounding.AwayFromZero);
                await _accounts.UpdateAsync(fd);
                await _txns.CreateAsync(new Transaction
                {
                    AccountId = fd.Id,
                    MemberId = fd.MemberId,
                    TypeCode = fd.TypeCode,
                    TxnType = TxnType.InterestCredit,
                    Amount = interest,
                    Narration = $"FD maturity interest ({tenorDays}d) credited on renewal",
                    EffectiveOnUtc = DateTime.UtcNow,
                    PostedByUserId = User?.Identity?.Name ?? "system",
                    BalanceAfterTxn = fd.PrincipalBalance
                });
                await _types.SafeUpdateTotalsAsync(fdType.TypeId, interest, interest);

                // 2) If renewal mode is PrincipalOnly: move interest out to savings
                if (req.RenewalMode == FdRenewalMode.PrincipalOnly)
                {
                    await Debit(fd, fdType, interest, "FD renewal: interest paid out to savings");
                    await Credit(savings, savingsType, interest, $"FD renewal interest payout from {req.TypeCode}");
                    fd = await _accounts.GetAsync(req.MemberId, req.TypeCode) ?? throw new InvalidOperationException("FD lost during operation.");
                }
                // If PrincipalPlusInterest, interest remains added to principal

                // 3) Resolve new tenor/rate and reset the FD term in-place
                var (newTenor, newRate) = ResolveFdRate(fdType.Attributes, req.TenorDays);
                fd.OpenedOnUtc = DateTime.UtcNow;
                fd.MaturityOnUtc = DateTime.UtcNow.AddDays(newTenor);

                var openPrincipal = fd.PrincipalBalance; // after possible interest payout
                var newSnap = new BsonDocument
                {
                    ["tenorDays"] = newTenor,
                    ["annualRate"] = newRate,
                    ["prematureThresholdMonths"] = GetInt(fd.Attributes?["fd"].AsBsonDocument, "prematureThresholdMonths", 3),
                    ["prematureAnnualRate"] = GetDecimal(fd.Attributes?["fd"].AsBsonDocument, "prematureAnnualRate", 0.03m),
                    ["openPrincipal"] = openPrincipal
                };
                if (fd.Attributes is null) fd.Attributes = new BsonDocument();
                fd.Attributes["fd"] = newSnap;

                await _accounts.UpdateAsync(fd);

                return Ok(new
                {
                    message = "FD renewed successfully.",
                    renewalMode = req.RenewalMode.ToString(),
                    newTenorDays = newTenor,
                    newAnnualRate = newRate,
                    newOpenPrincipal = openPrincipal,
                    maturityOnUtc = fd.MaturityOnUtc
                });
            }
            else
            {
                return BadRequest(new { message = "Action must be 'withdraw' or 'renew'." });
            }
        }

        // ------------------- Helpers -------------------

        private async Task<Member> GetActiveMemberOrThrow(string memberId)
        {
            var m = await _members.GetByMemberIdAsync(memberId) ?? throw new InvalidOperationException("Member not found.");
            if (m.Status != MemberStatus.Active) throw new InvalidOperationException("Member is inactive.");
            return m;
        }

        private async Task<AccountType> GetTypeOrThrow(string typeCode)
        {
            var t = await _types.GetByTypeIdAsync(typeCode) ?? throw new InvalidOperationException("Account type not found.");
            if (!t.IsActive) throw new InvalidOperationException("Account type is inactive.");
            return t;
        }

        private async Task<Account> GetSavingsDestinationOrThrow(string memberId, string savingsTypeCode)
        {
            // must exist and be active
            var acc = await _accounts.GetAsync(memberId, savingsTypeCode);
            if (acc == null) throw new InvalidOperationException("Destination savings account not found.");
            if (acc.Status != AccountStatus.Active) throw new InvalidOperationException("Destination savings account is not active.");

            var t = await _types.GetByTypeIdAsync(acc.TypeCode) ?? throw new InvalidOperationException("Destination account type missing.");
            if (t.Category != AccountCategory.MemberDeposits)
                throw new InvalidOperationException("Destination must be a member deposit account.");
            if (t.InterestMethod != InterestMethod.Quarterly_MinBalance && t.InterestMethod != InterestMethod.None)
                throw new InvalidOperationException("Destination savings must be a normal savings-like product.");
            return acc;
        }

       // Self-healing: if snapshot missing, rebuild it from product + dates and persist.
        // This version tolerates missing OpenedOnUtc / MaturityOnUtc by inferring from transactions
        // and product defaults.
        private async Task<BsonDocument> GetFdSnapshotOrRebuildAsync(Account fd)
        {
            // If snapshot already exists and is valid, return it
            if (fd.Attributes != null &&
                fd.Attributes.TryGetValue("fd", out var existing) &&
                existing.IsBsonDocument)
            {
                return existing.AsBsonDocument;
            }

            // Load product
            var fdType = await _types.GetByTypeIdAsync(fd.TypeCode)
             ?? throw new InvalidOperationException("FD product not found for this account");

// Ensure product attributes exist
var safeAttrs = await EnsureFdProductAttributes(fdType);

            // 1) Establish an opening date
            DateTime opened = fd.OpenedOnUtc;
            if (opened == default)
            {
                // Use earliest txn as opening date
                var allTxns = await _txns.GetForAccountAsync(fd.Id, null, null, null);
                var earliest = allTxns.OrderBy(t => t.EffectiveOnUtc).FirstOrDefault();
                if (earliest != null)
                {
                    opened = earliest.EffectiveOnUtc;
                    fd.OpenedOnUtc = opened; // persist below
                }
                else
                {
                    // no transactions either → use "now" as a last resort
                    opened = DateTime.UtcNow;
                    fd.OpenedOnUtc = opened;
                }
            }

            // 2) Decide tenor days
            // Prefer inferring from existing maturity; if absent, use product DefaultTenorDays
            int tenorDays;
            if (fd.MaturityOnUtc.HasValue)
            {
                var diff = (int)Math.Max(1, (fd.MaturityOnUtc.Value - opened).TotalDays);
                tenorDays = diff;
            }
            else
            {
                tenorDays = GetInt(fdType.Attributes, "DefaultTenorDays", 180);
                fd.MaturityOnUtc = opened.AddDays(tenorDays); // persist below
            }

            // 3) Resolve a matching rate from product RateTable (falls back to first row if exact tenor not found)
var (resolvedTenor, resolvedRate) = ResolveFdRate(safeAttrs, tenorDays);

            // 4) Premature policy from product attributes (with defaults)
            var thresholdMonths = GetInt(fdType.Attributes, "fdPrematureThresholdMonths", 3);
            var prematureRate   = GetDecimal(fdType.Attributes, "fdPrematureAnnualRate", 0.03m);

            // 5) Opening principal snapshot: if unknown, use current principal as best effort
            var openPrincipal = fd.PrincipalBalance;

            // 6) Build and persist snapshot
            var snapshot = new BsonDocument
            {
                ["tenorDays"] = resolvedTenor,
                ["annualRate"] = resolvedRate,
                ["prematureThresholdMonths"] = thresholdMonths,
                ["prematureAnnualRate"] = prematureRate,
                ["openPrincipal"] = openPrincipal
            };

            if (fd.Attributes is null) fd.Attributes = new BsonDocument();
            fd.Attributes["fd"] = snapshot;

            await _accounts.UpdateAsync(fd); // persist fixed dates + snapshot

            return snapshot;
        }

        // Ensure the FD product has valid Attributes (RateTable, DefaultTenorDays, premature policy).
// If missing or malformed, we write sane defaults back to the product document.
private async Task<BsonDocument> EnsureFdProductAttributes(AccountType fdType)
{
    var attrs = fdType.Attributes ?? new BsonDocument();
    bool changed = false;

    // RateTable must exist and be a non-empty array
    if (!attrs.TryGetValue("RateTable", out var rt) || !rt.IsBsonArray || rt.AsBsonArray.Count == 0)
    {
        attrs["RateTable"] = new BsonArray
        {
            new BsonDocument { ["TenorDays"] = 90,  ["Rate"] = 0.052m },
            new BsonDocument { ["TenorDays"] = 180, ["Rate"] = 0.055m },
            new BsonDocument { ["TenorDays"] = 365, ["Rate"] = 0.065m }
        };
        changed = true;
    }

    // DefaultTenorDays
    if (!attrs.TryGetValue("DefaultTenorDays", out var def) || !(def.IsInt32 || def.IsInt64 || def.IsDouble || def.IsDecimal128 || def.IsString))
    {
        attrs["DefaultTenorDays"] = 180;
        changed = true;
    }

    // Premature policy: months threshold
    if (!attrs.TryGetValue("fdPrematureThresholdMonths", out var th) || !(th.IsInt32 || th.IsInt64 || th.IsDouble || th.IsDecimal128 || th.IsString))
    {
        attrs["fdPrematureThresholdMonths"] = 3;
        changed = true;
    }

    // Premature policy: annual rate
    if (!attrs.TryGetValue("fdPrematureAnnualRate", out var pr) || !(pr.IsDecimal128 || pr.IsDouble || pr.IsInt32 || pr.IsInt64 || pr.IsString))
    {
        attrs["fdPrematureAnnualRate"] = 0.03m;
        changed = true;
    }

    if (changed)
    {
        fdType.Attributes = attrs;
        await _types.UpdateAsync(fdType.TypeId, fdType); // persist the repair
    }

    return attrs;
}




        private static BsonDocument GetFdSnapshotOrThrow(Account fd)
        {
            if (fd.Attributes is null || !fd.Attributes.TryGetValue("fd", out var v) || !v.IsBsonDocument)
                throw new InvalidOperationException("FD terms snapshot is missing.");
            return v.AsBsonDocument;
        }

        // Resolve tenor & rate from AccountType.Attributes
        // Expected structure:
        //  { "RateTable": [ { "TenorDays": 180, "Rate": 0.055 }, ... ], "DefaultTenorDays": 180,
        //    "fdPrematureThresholdMonths": 3, "fdPrematureAnnualRate": 0.03 }
        private static (int tenorDays, decimal rate) ResolveFdRate(BsonDocument attrs, int? preferredTenor)
{
    var tableVal = attrs.GetValue("RateTable", null);
    if (tableVal is null || !tableVal.IsBsonArray || tableVal.AsBsonArray.Count == 0)
        throw new InvalidOperationException("FD RateTable is missing or invalid after ensure step.");

    var table = tableVal.AsBsonArray;
    var defaultTenor = GetInt(attrs, "DefaultTenorDays", 180);
    var tenor = preferredTenor ?? defaultTenor;

    int chosenTenor = defaultTenor;
    decimal chosenRate = 0m;

    foreach (var row in table)
    {
        if (!row.IsBsonDocument) continue;
        var d = row.AsBsonDocument;
        var tDays = GetInt(d, "TenorDays", 0);
        var rate  = GetDecimal(d, "Rate", 0m);
        if (tDays == tenor)
        {
            chosenTenor = tDays;
            chosenRate  = rate;
            break;
        }
    }

    // If not found, fallback to the first row
    if (chosenRate <= 0m)
    {
        var first = table.FirstOrDefault();
        if (first != null && first.IsBsonDocument)
        {
            chosenTenor = GetInt(first.AsBsonDocument, "TenorDays", defaultTenor);
            chosenRate  = GetDecimal(first.AsBsonDocument, "Rate", 0m);
        }
    }

    if (chosenRate <= 0m)
        throw new InvalidOperationException("Could not resolve tenor/rate from RateTable.");

    return (chosenTenor, chosenRate);
}


        // Transactions & totals helpers
        private async Task Credit(Account acc, AccountType type, decimal amount, string narration)
        {
            acc.PrincipalBalance = decimal.Round(acc.PrincipalBalance + amount, 2, MidpointRounding.AwayFromZero);
            await _accounts.UpdateAsync(acc);

            await _txns.CreateAsync(new Transaction
            {
                AccountId = acc.Id,
                MemberId = acc.MemberId,
                TypeCode = acc.TypeCode,
                TxnType = TxnType.Deposit,
                Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero),
                Narration = narration,
                EffectiveOnUtc = DateTime.UtcNow,
                PostedByUserId = User?.Identity?.Name ?? "system",
                BalanceAfterTxn = acc.PrincipalBalance
            });

            await _types.SafeUpdateTotalsAsync(type.TypeId, totalBalanceDelta: amount, interestPaidDelta: 0m);
        }

        private async Task Debit(Account acc, AccountType type, decimal amount, string narration)
        {
            var resulting = acc.PrincipalBalance - amount;
            if (resulting < 0m)
                throw new InvalidOperationException("Insufficient funds in FD during payout.");

            acc.PrincipalBalance = decimal.Round(resulting, 2, MidpointRounding.AwayFromZero);
            await _accounts.UpdateAsync(acc);

            await _txns.CreateAsync(new Transaction
            {
                AccountId = acc.Id,
                MemberId = acc.MemberId,
                TypeCode = acc.TypeCode,
                TxnType = TxnType.Withdrawal,
                Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero),
                Narration = narration,
                EffectiveOnUtc = DateTime.UtcNow,
                PostedByUserId = User?.Identity?.Name ?? "system",
                BalanceAfterTxn = acc.PrincipalBalance
            });

            await _types.SafeUpdateTotalsAsync(type.TypeId, totalBalanceDelta: -amount, interestPaidDelta: 0m);
        }

        // BSON helpers
        private static int GetInt(BsonDocument? d, string name, int fallback)
        {
            if (d is null) return fallback;
            if (!d.TryGetValue(name, out var v)) return fallback;

            if (v.IsInt32)   return v.AsInt32;
            if (v.IsInt64)   return (int)v.AsInt64;
            if (v.IsDouble)  return (int)v.AsDouble;

            // Decimal128: parse via invariant string (works across driver versions)
            if (v.IsDecimal128)
            {
                var s = v.AsDecimal128.ToString(); // culture-invariant
                if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var dec))
                    return (int)decimal.Truncate(dec); // avoid rounding surprises
                return fallback;
            }

            // Strings like "90" or "0.055"
            if (v.IsString)
            {
                if (int.TryParse(v.AsString, NumberStyles.Integer, CultureInfo.InvariantCulture, out var asInt))
                    return asInt;

                if (decimal.TryParse(v.AsString, NumberStyles.Number, CultureInfo.InvariantCulture, out var asDec))
                    return (int)decimal.Truncate(asDec);
            }

            return fallback;
        }


        private static decimal GetDecimal(BsonDocument? d, string name, decimal fallback)
        {
            if (d is null) return fallback;
            if (!d.TryGetValue(name, out var v)) return fallback;

            if (v.IsDecimal128)
            {
                var s = v.AsDecimal128.ToString();
                if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var dec))
                    return dec;
                return fallback;
            }
            if (v.IsDouble)  return (decimal)v.AsDouble;
            if (v.IsInt32)   return v.AsInt32;
            if (v.IsInt64)   return v.AsInt64;

            if (v.IsString && decimal.TryParse(v.AsString, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
                return parsed;

            return fallback;
        }


        // Recursively convert BsonValue -> plain CLR types that System.Text.Json can serialize
        private static object? BsonToPlain(BsonValue v)
        {
            if (v.IsBsonNull) return null;
            if (v.IsString) return v.AsString;
            if (v.IsInt32) return v.AsInt32;
            if (v.IsInt64) return v.AsInt64;
            if (v.IsDouble) return (decimal)v.AsDouble;          // keep money as decimal-ish
            if (v.IsBoolean) return v.AsBoolean;
            if (v.IsDecimal128)
            {
                // culture-invariant parse to decimal
                if (decimal.TryParse(v.AsDecimal128.ToString(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var dec))
                    return dec;
                return v.AsDecimal128.ToString();
            }
            if (v.IsValidDateTime) return v.ToUniversalTime();

            if (v.IsBsonArray)
                return v.AsBsonArray.Select(BsonToPlain).ToList();

            if (v.IsBsonDocument)
            {
                var dict = new Dictionary<string, object?>();
                foreach (var el in v.AsBsonDocument)
                    dict[el.Name] = BsonToPlain(el.Value);
                return dict;
            }

            // Fallback: string form
            return v.ToString();
        }

        private static Dictionary<string, object>? BsonDocToDict(BsonDocument? d)
        {
            if (d is null) return null;
            var result = new Dictionary<string, object?>();
            foreach (var el in d)
                result[el.Name] = BsonToPlain(el.Value);
            // cast away nullable to satisfy DTO type (we never put null keys)
            return result!;
        }

        private static EvCharge.Api.DTOs.FdAccountResponse MapFd(Account a)
        {
            return new EvCharge.Api.DTOs.FdAccountResponse
            {
                Id = a.Id,
                MemberId = a.MemberId,
                TypeCode = a.TypeCode,
                Category = a.Category,
                Status = a.Status,
                PrincipalBalance = a.PrincipalBalance,
                AccruedInterest = a.AccruedInterest,
                OpenedOnUtc = a.OpenedOnUtc,
                MaturityOnUtc = a.MaturityOnUtc,
                LinkedSavingsAccountNumber = a.LinkedSavingsAccountNumber,
                Attributes = BsonDocToDict(a.Attributes)
            };
        }

        // GET /api/fd/preview/maturity?memberId=1001&typeCode=FD
        [HttpGet("preview/maturity")]
        public async Task<IActionResult> PreviewMaturity([FromQuery] string memberId, [FromQuery] string typeCode)
        {
            if (string.IsNullOrWhiteSpace(memberId) || string.IsNullOrWhiteSpace(typeCode))
                return BadRequest(new { message = "memberId and typeCode are required." });

            var fd = await _accounts.GetAsync(memberId, typeCode);
            if (fd == null) return NotFound(new { message = "FD account not found." });
            if (fd.Status != AccountStatus.Active) return BadRequest(new { message = "FD is not active." });

            var snap = await GetFdSnapshotOrRebuildAsync(fd);
            var (tenorDays, annualRate, _, _, _) = ReadSnapshot(snap);

            if (tenorDays <= 0 || annualRate <= 0m)
                return BadRequest(new { message = "FD snapshot tenor/rate is invalid." });

            var principal = fd.PrincipalBalance;
            var interest = ComputeFdMaturityInterest(principal, tenorDays, annualRate);
            var payout = Round2(principal + interest);

            var resp = new EvCharge.Api.DTOs.FdMaturityPreviewResponse
            {
                MemberId = memberId,
                TypeCode = typeCode,
                Principal = principal,
                TenorDays = tenorDays,
                AnnualRate = annualRate,
                InterestAtMaturity = interest,
                PayoutAtMaturity = payout,
                OpenedOnUtc = fd.OpenedOnUtc,
                MaturityOnUtc = fd.MaturityOnUtc
            };
            return Ok(resp);
        }

        // GET /api/fd/preview/premature?memberId=1001&typeCode=FD&asOfUtc=2025-11-29T00:00:00Z
        [HttpGet("preview/premature")]
        public async Task<IActionResult> PreviewPremature(
            [FromQuery] string memberId,
            [FromQuery] string typeCode,
            [FromQuery] DateTime? asOfUtc)
        {
            if (string.IsNullOrWhiteSpace(memberId) || string.IsNullOrWhiteSpace(typeCode))
                return BadRequest(new { message = "memberId and typeCode are required." });

            var fd = await _accounts.GetAsync(memberId, typeCode);
            if (fd == null) return NotFound(new { message = "FD account not found." });
            if (fd.Status != AccountStatus.Active) return BadRequest(new { message = "FD is not active." });

            var snap = await GetFdSnapshotOrRebuildAsync(fd);
            var (_, _, thresholdMonths, prematureAnnualRate, _) = ReadSnapshot(snap);

            var principal = fd.PrincipalBalance;
            var asOf = asOfUtc.HasValue
    ? DateTime.SpecifyKind(asOfUtc.Value, DateTimeKind.Utc)
    : DateTime.UtcNow;


            var interest = ComputeFdPrematureInterest(principal, fd.OpenedOnUtc, asOf, thresholdMonths, prematureAnnualRate);
            var payout = Round2(principal + interest);

            var resp = new EvCharge.Api.DTOs.FdPrematurePreviewResponse
            {
                MemberId = memberId,
                TypeCode = typeCode,
                Principal = principal,
                PrematureThresholdMonths = thresholdMonths,
                PrematureAnnualRate = prematureAnnualRate,
                ElapsedDays = (int)Math.Max(0, (asOf - fd.OpenedOnUtc).TotalDays),
                EligibleForPrematureInterest = interest > 0m,
                InterestIfClosed = interest,
                PayoutIfClosed = payout,
                OpenedOnUtc = fd.OpenedOnUtc,
                AsOfUtc = asOf
            };
            return Ok(resp);
        }



    }
}
