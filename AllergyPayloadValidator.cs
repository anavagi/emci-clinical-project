using FluentValidation;
using EmciDemo.Models;
using System.Linq;

namespace EmciDemo.Validators
{
    public class AllergyPayloadValidator : AbstractValidator<AllergyPayloadDto>
    {
        public AllergyPayloadValidator()
        {
            RuleFor(x => x.PatientId)
                .NotEmpty().WithMessage("El ID del paciente es obligatorio.");

            RuleFor(x => x.Allergies)
                .NotNull().WithMessage("La lista de alergias no puede ser nula.");

            RuleForEach(x => x.Allergies).SetValidator(new AllergyItemValidator());
        }
    }

    public class AllergyItemValidator : AbstractValidator<AllergyItemDto>
    {
        private readonly string[] _validTypes = { "Medicamento", "Alimento", "Ambiental", "Otro" };
        private readonly string[] _validSeverities = { "Leve", "Moderada", "Severa" };
        private readonly string[] _validStatuses = { "Activa", "Inactiva", "Resuelta" };

        public AllergyItemValidator()
        {
            RuleFor(x => x.Allergen)
                .NotEmpty().WithMessage("El nombre del alérgeno es obligatorio.")
                .MaximumLength(150);

            RuleFor(x => x.AllergyType)
                .NotEmpty().WithMessage("El tipo de alergia es obligatorio.")
                .Must(type => _validTypes.Contains(type))
                .WithMessage($"El tipo de alergia debe ser uno de: {string.Join(", ", _validTypes)}");

            RuleFor(x => x.Severity)
                .NotEmpty().WithMessage("La severidad es obligatoria.")
                .Must(sev => _validSeverities.Contains(sev))
                .WithMessage($"La severidad debe ser una de: {string.Join(", ", _validSeverities)}");

            RuleFor(x => x.Status)
                .NotEmpty().WithMessage("El estado es obligatorio.")
                .Must(st => _validStatuses.Contains(st))
                .WithMessage("El estado de la alergia debe ser Activa, Inactiva o Resuelta.");
        }
    }
}
