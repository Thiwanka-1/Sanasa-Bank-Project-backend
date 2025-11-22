// =============================================
// File: AccountTypesController.cs
// Description: Admin management for Account Types catalog
// Rules:
//  - Auth required
//  - TypeId & Category are immutable after creation
//  - Totals are read-only here (maintained by accounts/transactions)
// =============================================
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EvCharge.Api.Repositories;
using EvCharge.Api.DTOs;
using EvCharge.Api.Domain;
using MongoDB.Bson;

namespace EvCharge.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class AccountTypesController : ControllerBase
    {
        private readonly AccountTypeRepository _repo;

        public AccountTypesController(IConfiguration config)
        {
            _repo = new AccountTypeRepository(config);
        }

        private static AccountTypeResponse Map(AccountType t) => new AccountTypeResponse
        {
            Id = t.Id,
            TypeId = t.TypeId,
            Category = t.Category,
            TypeName = t.TypeName,
            InterestRateAnnual = t.InterestRateAnnual,
            InterestMethod = t.InterestMethod,
            MinimumBalance = t.MinimumBalance,
            IsActive = t.IsActive,
            Attributes = (t.Attributes is null) ? null : MongoDB.Bson.Serialization.BsonSerializer.Deserialize<Dictionary<string, object>>(t.Attributes),
            TotalBalanceAllAccounts = t.TotalBalanceAllAccounts,
            TotalInterestPaidToDate = t.TotalInterestPaidToDate
        };

        // GET /api/accounttypes
        [HttpGet]
        public async Task<ActionResult<List<AccountTypeResponse>>> GetAll()
        {
            var list = await _repo.GetAllAsync();
            return list.Select(Map).ToList();
        }

        // GET /api/accounttypes/{typeId}
        [HttpGet("{typeId}")]
        public async Task<ActionResult<AccountTypeResponse>> GetByTypeId(string typeId)
        {
            var entity = await _repo.GetByTypeIdAsync(typeId);
            if (entity == null) return NotFound();
            return Map(entity);
        }

        // POST /api/accounttypes
        [HttpPost]
        public async Task<ActionResult<AccountTypeResponse>> Create([FromBody] CreateAccountTypeRequest req)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            // Duplicate check
            var exists = await _repo.GetByTypeIdAsync(req.TypeId);
            if (exists != null) return Conflict(new { message = "TypeId already exists." });

            var attrsBson = req.Attributes is null
                ? null
                : MongoDB.Bson.Serialization.BsonSerializer.Deserialize<BsonDocument>(
                    System.Text.Json.JsonSerializer.Serialize(req.Attributes)
                  );

            var entity = new AccountType
            {
                TypeId = req.TypeId,
                Category = req.Category,
                TypeName = req.TypeName,
                InterestRateAnnual = req.InterestRateAnnual,
                InterestMethod = req.InterestMethod,
                MinimumBalance = req.MinimumBalance,
                IsActive = true,
                Attributes = attrsBson,
                TotalBalanceAllAccounts = 0m,
                TotalInterestPaidToDate = 0m
            };

            await _repo.CreateAsync(entity);
            var resp = Map(entity);
            return CreatedAtAction(nameof(GetByTypeId), new { typeId = entity.TypeId }, resp);
        }

        // PUT /api/accounttypes/{typeId}
        // Immutable: TypeId, Category
        [HttpPut("{typeId}")]
        public async Task<IActionResult> Update(string typeId, [FromBody] UpdateAccountTypeRequest req)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            var existing = await _repo.GetByTypeIdAsync(typeId);
            if (existing == null) return NotFound();

            existing.TypeName = req.TypeName;
            existing.InterestRateAnnual = req.InterestRateAnnual;
            existing.InterestMethod = req.InterestMethod;
            existing.MinimumBalance = req.MinimumBalance;
            existing.Attributes = req.Attributes is null
                ? null
                : MongoDB.Bson.Serialization.BsonSerializer.Deserialize<BsonDocument>(
                    System.Text.Json.JsonSerializer.Serialize(req.Attributes)
                  );

            await _repo.UpdateAsync(typeId, existing);
            return NoContent();
        }

        // PATCH /api/accounttypes/{typeId}/status
        [HttpPatch("{typeId}/status")]
        public async Task<IActionResult> ChangeStatus(string typeId, [FromBody] ChangeAccountTypeStatusRequest req)
        {
            var existing = await _repo.GetByTypeIdAsync(typeId);
            if (existing == null) return NotFound();

            await _repo.SetActiveAsync(typeId, req.IsActive);
            return Ok(new { message = $"AccountType {typeId} active = {req.IsActive}" });
        }
    }
}
