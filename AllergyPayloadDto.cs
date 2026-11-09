using System.Collections.Generic;

namespace EmciDemo.Models
{
    public class AllergyPayloadDto
    {
        public string PatientId { get; set; } = string.Empty;
        public string? EpisodeId { get; set; }
        public string ClinicalSummary { get; set; } = string.Empty;
        public List<AllergyItemDto> Allergies { get; set; } = new();
    }

    public class AllergyItemDto
    {
        public string AllergenCode { get; set; } = string.Empty;
        public string Allergen { get; set; } = string.Empty;
        public string AllergyType { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public string Reaction { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string OnsetDate { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
    }
}
