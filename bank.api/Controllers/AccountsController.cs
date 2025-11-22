// =============================================
// File: AccountsController.cs
// Description: Open/close/status accounts and deposit/withdraw transactions
// Validations:
//  - Member must exist; Member.Status Active for operations
//  - AccountType must exist, be active, and category must match Member.Type
//  - One account per (MemberId, TypeCode)
//  - MinimumBalance enforced on withdrawals
//  - AccountType totals maintained (safe updater)
// =============================================
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using EvCharge.Api.Domain;
using EvCharge.Api.DTOs;
using EvCharge.Api.Repositories;

namespace EvCharge.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class AccountsController : ControllerBase
    {
        private readonly MemberRepository _members;
        private readonly AccountTypeRepository _types;
        private readonly AccountRepository _accounts;
        private readonly TransactionRepository _txns;

        public AccountsController(IConfiguration config)
        {
            _members  = new MemberRepository(config);
            _types    = new AccountTypeRepository(config);
            _accounts = new AccountRepository(config);
            _txns     = new TransactionRepository(config);
        }

        private static AccountResponse Map(Account a) => new AccountResponse
        {
            Id = a.Id,
            MemberId = a.MemberId,
            TypeCode = a.TypeCode,
            Category = a.Category,
            Status = a.Status,
            PrincipalBalance = a.PrincipalBalance,
            AccruedInterest = a.AccruedInterest,
            OpenedOnUtc = a.OpenedOnUtc
        };

        // GET /api/accounts/{memberId}
        [HttpGet("{memberId}")]
        public async Task<ActionResult<List<AccountResponse>>> GetByMember(string memberId)
        {
            var list = await _accounts.GetByMemberAsync(memberId);
            return list.Select(Map).ToList();
        }

        // GET /api/accounts/{memberId}/{typeCode}
        [HttpGet("{memberId}/{typeCode}")]
        public async Task<ActionResult<AccountResponse>> GetOne(string memberId, string typeCode)
        {
            var acc = await _accounts.GetAsync(memberId, typeCode);
            if (acc == null) return NotFound();
            return Map(acc);
        }

        // POST /api/accounts (open)
        [HttpPost]
        public async Task<ActionResult<AccountResponse>> Open([FromBody] OpenAccountRequest req)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            // Member check
            var member = await _members.GetByMemberIdAsync(req.MemberId);
            if (member == null) return NotFound(new { message = "Member not found." });
            if (member.Status != MemberStatus.Active)
                return BadRequest(new { message = "Cannot open account: member is inactive." });

            // Type check
            var type = await _types.GetByTypeIdAsync(req.TypeCode);
            if (type == null || !type.IsActive)
                return BadRequest(new { message = "Account type not found or inactive." });

            // Category consistency: Member vs NonMember
            var isMember = member.Type == PartyType.Member;
            if (isMember && type.Category != AccountCategory.MemberDeposits)
                return BadRequest(new { message = "Member can only open MemberDeposits types." });
            if (!isMember && type.Category != AccountCategory.NonMemberDeposits)
                return BadRequest(new { message = "Non-member can only open NonMemberDeposits types." });

            // One account per (MemberId, TypeCode)
            var existing = await _accounts.GetAsync(req.MemberId, req.TypeCode);
            if (existing != null) return Conflict(new { message = "Account already exists for this MemberId and TypeCode." });

            // Create account
            var acc = new Account
            {
                MemberId = req.MemberId,
                TypeCode = req.TypeCode,
                Category = type.Category,
                Status = AccountStatus.Active,
                PrincipalBalance = 0m,
                AccruedInterest = 0m,
                OpenedOnUtc = DateTime.UtcNow
            };
            await _accounts.CreateAsync(acc);

            // Initial deposit (optional)
            if (req.InitialDeposit > 0m)
            {
                await ApplyDeposit(acc, type, req.InitialDeposit, "Initial deposit");
            }

            var fresh = await _accounts.GetAsync(req.MemberId, req.TypeCode);
            return CreatedAtAction(nameof(GetOne), new { memberId = req.MemberId, typeCode = req.TypeCode }, Map(fresh!));
        }

        // PATCH /api/accounts/{memberId}/{typeCode}/status
        [HttpPatch("{memberId}/{typeCode}/status")]
        public async Task<IActionResult> ChangeStatus(string memberId, string typeCode, [FromBody] ChangeAccountStatusRequest req)
        {
            var acc = await _accounts.GetAsync(memberId, typeCode);
            if (acc == null) return NotFound();

            // if closing, ensure balance is zero (policy)
            if (req.Status == AccountStatus.Closed && acc.PrincipalBalance != 0m)
                return BadRequest(new { message = "Cannot close account with non-zero balance." });

            await _accounts.SetStatusAsync(memberId, typeCode, req.Status);
            return Ok(new { message = $"Account {memberId}/{typeCode} status set to {req.Status}" });
        }

        // POST /api/accounts/deposit
        [HttpPost("deposit")]
        public async Task<IActionResult> Deposit([FromBody] DepositRequest req)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            var acc = await _accounts.GetAsync(req.MemberId, req.TypeCode);
            if (acc == null) return NotFound(new { message = "Account not found." });

            var member = await _members.GetByMemberIdAsync(acc.MemberId);
            if (member == null) return NotFound(new { message = "Owner not found." });
            if (member.Status != MemberStatus.Active)
                return BadRequest(new { message = "Member is inactive." });

            if (acc.Status != AccountStatus.Active)
                return BadRequest(new { message = "Account is not active." });

            var type = await _types.GetByTypeIdAsync(acc.TypeCode);
            if (type == null || !type.IsActive)
                return BadRequest(new { message = "Account type not found or inactive." });

            await ApplyDeposit(acc, type, req.Amount, req.Narration ?? "Deposit");
            return Ok(new { message = "Deposit successful." });
        }

        // POST /api/accounts/withdraw
        [HttpPost("withdraw")]
        public async Task<IActionResult> Withdraw([FromBody] WithdrawRequest req)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            var acc = await _accounts.GetAsync(req.MemberId, req.TypeCode);
            if (acc == null) return NotFound(new { message = "Account not found." });

            var member = await _members.GetByMemberIdAsync(acc.MemberId);
            if (member == null) return NotFound(new { message = "Owner not found." });
            if (member.Status != MemberStatus.Active)
                return BadRequest(new { message = "Member is inactive." });

            if (acc.Status != AccountStatus.Active)
                return BadRequest(new { message = "Account is not active." });

            var type = await _types.GetByTypeIdAsync(acc.TypeCode);
            if (type == null || !type.IsActive)
                return BadRequest(new { message = "Account type not found or inactive." });

            // Minimum balance enforcement
            var resulting = acc.PrincipalBalance - req.Amount;
            if (resulting < 0m)
                return BadRequest(new { message = "Insufficient funds." });

            if (resulting < type.MinimumBalance)
                return BadRequest(new { message = $"Withdrawal would breach minimum balance of {type.MinimumBalance:0.00}." });

            await ApplyWithdrawal(acc, type, req.Amount, req.Narration ?? "Withdrawal");
            return Ok(new { message = "Withdrawal successful." });
        }

        // GET /api/accounts/{memberId}/{typeCode}/transactions
        [HttpGet("{memberId}/{typeCode}/transactions")]
        public async Task<ActionResult<List<Transaction>>> GetTransactions(
            string memberId, string typeCode, [FromQuery] DateTime? dateFromUtc, [FromQuery] DateTime? dateToUtc, [FromQuery] string? type)
        {
            var acc = await _accounts.GetAsync(memberId, typeCode);
            if (acc == null) return NotFound();

            TxnType? filterType = null;
            if (!string.IsNullOrWhiteSpace(type) && Enum.TryParse<TxnType>(type, true, out var parsed))
                filterType = parsed;

            var list = await _txns.GetForAccountAsync(acc.Id, dateFromUtc, dateToUtc, filterType);
            return list;
        }

        // ---------- Helpers (deposit/withdraw + totals maintenance) ----------

        private async Task ApplyDeposit(Account acc, AccountType type, decimal amount, string narration)
        {
            // Update account balance
            acc.PrincipalBalance = Decimal.Round(acc.PrincipalBalance + amount, 2, MidpointRounding.AwayFromZero);
            await _accounts.UpdateAsync(acc);

            // Write transaction
            var txn = new Transaction
            {
                AccountId = acc.Id,
                MemberId = acc.MemberId,
                TypeCode = acc.TypeCode,
                TxnType = TxnType.Deposit,
                Amount = Decimal.Round(amount, 2, MidpointRounding.AwayFromZero),
                Narration = narration,
                EffectiveOnUtc = DateTime.UtcNow,
                PostedByUserId = User?.Identity?.Name ?? "system",
                BalanceAfterTxn = acc.PrincipalBalance
            };
            await _txns.CreateAsync(txn);

            // Update AccountType totals (balance up) - safe updater to auto-fix legacy string fields
            await _types.SafeUpdateTotalsAsync(type.TypeId, totalBalanceDelta: amount, interestPaidDelta: 0m);
        }

        private async Task ApplyWithdrawal(Account acc, AccountType type, decimal amount, string narration)
        {
            acc.PrincipalBalance = Decimal.Round(acc.PrincipalBalance - amount, 2, MidpointRounding.AwayFromZero);
            await _accounts.UpdateAsync(acc);

            var txn = new Transaction
            {
                AccountId = acc.Id,
                MemberId = acc.MemberId,
                TypeCode = acc.TypeCode,
                TxnType = TxnType.Withdrawal,
                Amount = Decimal.Round(amount, 2, MidpointRounding.AwayFromZero),
                Narration = narration,
                EffectiveOnUtc = DateTime.UtcNow,
                PostedByUserId = User?.Identity?.Name ?? "system",
                BalanceAfterTxn = acc.PrincipalBalance
            };
            await _txns.CreateAsync(txn);

            // Totals (balance down) - safe updater
            await _types.SafeUpdateTotalsAsync(type.TypeId, totalBalanceDelta: -amount, interestPaidDelta: 0m);
        }
    }
}
