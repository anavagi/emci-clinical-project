using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FluentValidation;
using EmciDemo.Models;
using System.Text.Json;

namespace EmciDemo.Controllers
{
    [ApiController]
    [Route("api/clinical/allergies")]
    public class AllergiesController : ControllerBase
    {
        private readonly IValidator<AllergyPayloadDto> _validator;
        private readonly DbContext _context;

        public AllergiesController(IValidator<AllergyPayloadDto> validator, DbContext context)
        {
            _validator = validator;
            _context = context;
        }

        [HttpPost]
        public async Task<IActionResult> SaveAllergies([FromBody] AllergyPayloadDto payload)
        {
            var validationResult = await _validator.ValidateAsync(payload);
            if (!validationResult.IsValid)
            {
                return BadRequest(validationResult.Errors.Select(e => e.ErrorMessage));
            }

            var record = new ClinicalDocumentRecord
            {
                Id = Guid.NewGuid(),
                PatientId = Guid.Parse(payload.PatientId),
                EpisodeId = string.IsNullOrEmpty(payload.EpisodeId) ? null : Guid.Parse(payload.EpisodeId),
                TemplateCode = "CLIN_REGISTRO_ALERGIAS_V1",
                Status = "Active",
                RecordedAt = DateTime.UtcNow,
                AuthorId = User.Identity?.Name ?? "DR_SYSTEM",
                PayloadJson = JsonSerializer.Serialize(payload)
            };

            _context.Set<ClinicalDocumentRecord>().Add(record);
            await _context.SaveChangesAsync();

            return Ok(new { Message = "Alergias guardadas correctamente bajo el estándar EMCI", RecordId = record.Id });
        }
    }
}
