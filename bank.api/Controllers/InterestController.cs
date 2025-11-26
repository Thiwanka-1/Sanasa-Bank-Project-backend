// =============================================
// File: InterestController.cs
// Description: Preview & run quarterly interest for savings-like products
// Also: list batches, get batch by id, reverse a batch (safe)
// =============================================
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EvCharge.Api.DTOs;
using EvCharge.Api.Services;
using EvCharge.Api.Domain;

namespace EvCharge.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class InterestController : ControllerBase
    {
        private readonly InterestService _svc;

        public InterestController(IConfiguration config)
        {
            _svc = new InterestService(config);
        }

        // GET /api/interest/preview?typeCode=A1&quarter=2025Q1
        [HttpGet("preview")]
        public async Task<ActionResult<InterestPreviewResponse>> Preview([FromQuery] string typeCode, [FromQuery] string quarter)
        {
            try
            {
                var res = await _svc.PreviewAsync(new InterestPreviewRequest { TypeCode = typeCode, Quarter = quarter });
                return Ok(res);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // POST /api/interest/run   body: { "typeCode":"A1", "quarter":"2025Q1" }
        [HttpPost("run")]
        public async Task<ActionResult> Run([FromBody] InterestRunRequest req)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);
            try
            {
                var userId = User?.Identity?.Name ?? "system";
                var batch = await _svc.RunAsync(req, userId);
                return Ok(new
                {
                    message = $"Interest posted successfully for {req.TypeCode} in {req.Quarter}.",
                    batchId = batch.Id,
                    totalAccounts = batch.TotalAccounts,
                    totalInterest = batch.TotalInterest,
                    periodStartUtc = batch.PeriodStartUtc,
                    periodEndUtc = batch.PeriodEndUtc,
                    postedAtUtc = batch.PostedAtUtc
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // GET /api/interest/batches?typeCode=A1
        [HttpGet("batches")]
        public async Task<ActionResult<List<InterestBatchDto>>> ListBatches([FromQuery] string? typeCode)
        {
            var list = await _svc.ListBatchesAsync(typeCode);
            var dto = list.Select(b => new InterestBatchDto
            {
                Id = b.Id,
                TypeCode = b.TypeCode,
                Quarter = b.QuarterKey,
                PeriodStartUtc = b.PeriodStartUtc,
                PeriodEndUtc = b.PeriodEndUtc,
                PostedAtUtc = b.PostedAtUtc,
                TotalAccounts = b.TotalAccounts,
                TotalInterest = b.TotalInterest,
                IsReversed = b.IsReversed,
                ReversedAtUtc = b.ReversedAtUtc
            }).ToList();

            return Ok(dto);
        }

        // GET /api/interest/batches/{id}
        [HttpGet("batches/{id}")]
        public async Task<ActionResult<InterestBatchDto>> GetBatch(string id)
        {
            var b = await _svc.GetBatchAsync(id);
            if (b == null) return NotFound(new { message = "Batch not found." });

            return Ok(new InterestBatchDto
            {
                Id = b.Id,
                TypeCode = b.TypeCode,
                Quarter = b.QuarterKey,
                PeriodStartUtc = b.PeriodStartUtc,
                PeriodEndUtc = b.PeriodEndUtc,
                PostedAtUtc = b.PostedAtUtc,
                TotalAccounts = b.TotalAccounts,
                TotalInterest = b.TotalInterest,
                IsReversed = b.IsReversed,
                ReversedAtUtc = b.ReversedAtUtc
            });
        }

        // POST /api/interest/reverse   body: { "typeCode":"A1", "quarter":"2025Q1" }
        [HttpPost("reverse")]
        public async Task<ActionResult> Reverse([FromBody] InterestReverseRequest req)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);
            try
            {
                var userId = User?.Identity?.Name ?? "system";
                await _svc.ReverseAsync(req, userId);
                return Ok(new { message = $"Interest batch reversed for {req.TypeCode} in {req.Quarter}." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
