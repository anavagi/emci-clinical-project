using System;

namespace EmciDemo.Models
{
    /// <summary>
    /// Entidad relacional base del Estándar de Modelado Clínico Interno (EMCI).
    /// Mantiene el núcleo estable (Paciente, Episodio, Estado y Auditoría) 
    /// y encapsula el contenido dinámico del formulario en PayloadJson.
    /// </summary>
    public class ClinicalDocumentRecord
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// ID del Paciente al que pertenece el registro (Relación Transversal).
        /// </summary>
        public Guid PatientId { get; set; }

        /// <summary>
        /// ID del Episodio / Ingreso / Expediente al que pertenece el registro.
        /// Opcional (null) si el dato es transversal como las Alergias de por vida.
        /// Obligatorio si es un acto contextual como un Consentimiento o Nota Quirúrgica.
        /// </summary>
        public Guid? EpisodeId { get; set; }

        /// <summary>
        /// Código jerárquico del formulario o plantilla de origen (Ej: CLIN_REGISTRO_ALERGIAS_V1).
        /// </summary>
        public string TemplateCode { get; set; } = string.Empty;

        /// <summary>
        /// Estado del workflow (Draft, PendingSignature, Signed, Active, Revoked).
        /// Vive en el núcleo relacional para permitir filtrados rápidos en SQL.
        /// </summary>
        public string Status { get; set; } = "Active";

        /// <summary>
        /// Fecha y hora de creación del documento clínico en UTC.
        /// </summary>
        public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Identificador del profesional o sistema que registró la información.
        /// </summary>
        public string AuthorId { get; set; } = string.Empty;

        /// <summary>
        /// Contenido dinámico del formulario en formato JSON plano serializado.
        /// </summary>
        public string PayloadJson { get; set; } = string.Empty;
    }
}
