# EMCI (Estándar de Modelado Clínico Interno) - Documentación Técnica y Modelo .NET

Este documento detalla la arquitectura híbrida basada en los principios semánticos de openEHR adaptada para bases de datos relacionales internas en .NET.

---

## 1. ¿Qué es el EMCI?

El **Estándar de Modelado Clínico Interno (EMCI)** es un enfoque pragmático para sistemas de información clínica (HIS/EHR). En lugar de implementar un motor complejo de arquetipos y diccionarios de
metadatos dinámicos puros, el EMCI separa la persistencia en dos capas complementarias:

- **El Núcleo Relacional Estable:** Identificadores universales (`ExpedientId`, `EpisodeId`), clasificador de plantilla (`TemplateCode`), estado del flujo de trabajo (`Status`), marcas temporales y
auditoría.
- **El Contenido Clínico Dinámico (`PayloadJson`):** Estructuras clínicas flexibles en formato JSON que evolucionan sin alterar el esquema físico de la base de datos (evitando migraciones continuas de
EF Core por cambios en formularios clínicos).

---

## 2. Ventajas del Modelo

1. **Cero Migraciones por Cambios de Formularios:** Si el personal clínico requiere nuevos atributos en una escala de valoración, triaje, consentimiento o anamnesis, no se modifica la estructura DDL de
la base de datos; únicamente se actualiza el DTO y su validador en .NET.
2. **Coherencia Arquitectónica:** Todos los módulos clínicos de la organización (Cardiología, Urgencias, Farmacia, Quirófano, etc.) persisten sus actos y documentos asistenciales bajo el mismo
contenedor estandarizado (`ClinicalDocumentRecord`).
3. **Validación Robusta en Código:** Uso de **FluentValidation** para garantizar la integridad semántica y clínica (rangos fisiológicos, catálogos permitidos, obligatoriedad condicional) antes de
persistir el documento.
4. **Alto Rendimiento en Consultas Relacionales:** Uso de índices SQL nativos sobre las columnas de contexto (`ExpedientId`, `EpisodeId`, `Status`, `TemplateCode`) combinados con proyección y consulta
nativa sobre columnas JSON (`ToJson()`).

---

## 3. Separación entre Datos Transversales y el Contenedor Universal Contextual

Los datos en salud siguen una jerarquía estructural que el EMCI divide en dos niveles físicos:

- **Entidades Transversales del Paciente (Núcleo Maestro):**
  Representan el estado consolidado y permanente de por vida del paciente al margen de un episodio concreto (ej. `Patients` o el resumen activo de alergias en `PatientAllergies`). Se vinculan
  directamente mediante `ExpedientId`, no dependen de un `EpisodeId` para su vigencia clínica y utilizan columnas JSON mapeadas (`OwnsMany(...).ToJson()`) para absorber sus listas de detalles
  (manifestaciones, agentes causales, asociaciones) sin generar tablas satélites innecesarias.
- **Contenedor Universal Contextual (`ClinicalDocumentRecords`):**
  Encapsula todos los actos asistenciales, formularios, evolutivos, consentimientos informados, notas quirúrgicas y composiciones documentales registradas en el contexto de la atención clínica. Requiere
  vincularse a `ExpedientId` y `EpisodeId` (asociado al episodio, ingreso, consulta o intervención) junto con su `TemplateCode`.

### Regla de Oro EMCI:

> Los identificadores estructurales de contexto (`ExpedientId`, `EpisodeId`, `TemplateCode`, `Status`) residen **exclusivamente en columnas relacionales SQL** para indexación y filtrado de alto
rendimiento. El detalle clínico variable del acto asistencial reside dentro de **`PayloadJson`** sin duplicar los identificadores de la cabecera.

---

## 4. Globalización de `ClinicalDocumentRecords`

`ClinicalDocumentRecords` no es una tabla perteneciente a un único módulo vertical, sino el **Contenedor Universal Contextual (Repositorio Documental Clínico / CDR)** del que cuelgan todos los actos,
formularios, evolutivos y procesos asistenciales del expediente del paciente a lo largo de su historia clínica.

### ¿Por qué debe estar globalizada?

1. **Una sola tabla para múltiples dominios clínicos (`TemplateCode` como discriminador):**
   En lugar de proliferar decenas de tablas específicas por especialidad (`SurgicalNotes`, `TriageRecords`, `InformedConsents`, `PatientAllergyAudits`, `NursingAssessments`), todos los procesos
   contextuales escriben en `ClinicalDocumentRecords`. La diferenciación semántica se realiza mediante la columna `TemplateCode` y el contrato del `PayloadJson`:
   - `TemplateCode = "EMCI_ALLERGY_COMPOSITION_V1"` → Documento/auditoría de composición de alergias (`AiR`).
   - `TemplateCode = "EMCI_URG_TRIAGE_V1"` → Informe de triaje de urgencias.
   - `TemplateCode = "EMCI_INFORMED_CONSENT_V1"` → Consentimiento informado quirúrgico.

2. **Visión 360º del Episodio y del Expediente sin `JOIN`s masivos:**
   Al estar globalizada e indexada por `ExpedientId` y `EpisodeId`, cualquier visor clínico (cronología del paciente o carpeta asistencial de un episodio) recupera toda la actividad clínica de un
   ingreso o consulta mediante una única consulta SQL:

   ```sql
   SELECT Id, TemplateCode, Status, RecordedAt, PayloadJson
   FROM dbo.ClinicalDocumentRecords
   WHERE ExpedientId = @expedientId AND EpisodeId = @episodeId
   ORDER BY RecordedAt DESC;
   ```

---

## 5. Gestión de Documentos Pesados, Workflows, Auditoría y Catálogos

| Necesidad Operativa | Estrategia EMCI | Explicación Técnica |
| :--- | :--- | :--- |
| **PDFs y Binarios (Base64)** | Storage Externo (Azure Blob / S3) | **Prohibido almacenar binarios o Base64 pesados en la BD**. El archivo físico se envía a Object Storage y en el `PayloadJson` solo se almacena su metadato (ID/URL). |
| **Rastreo de Estados / Workflows** | Columna `Status` en SQL | El estado del ciclo de vida (`Draft`, `PendingSignature`, `Signed`, `Active`, `Revoked`) reside en columna SQL indexada para alimentar
bandejas de trabajo sin deserializar JSON. |
| **Auditoría de Cambios Técnicos** | Tabla Relacional `AuditLogs` | Integración a nivel de `DbContext` (ej. interceptores de EF Core / `Audit.NET`) para trazar quién creó, modificó o anuló un registro.
|
| **Catálogos Maestros** | Tablas Relacionales de Referencia | Catálogos normalizados (SNOMED-CT, CIE-10, ATC) en tablas SQL de referencia. Los códigos normalizados seleccionados se persisten dentro del
`PayloadJson`. |


### Desglose de Separación de Responsabilidades en el Contenedor

1. **El Estado del Workflow (`Status`) → Columna Relacional SQL:**
   El estado operativo del documento (`Draft`, `PendingSignature`, `Signed`, `Revoked`) se ubica en la columna relacional `Status` de `ClinicalDocumentRecords`. Esto permite ejecutar consultas
   operativas inmediatas (ej. *"Obtener todos los consentimientos pendientes de firma del día actual"*) utilizando índices SQL sin penalizar la CPU parseando documentos JSON. Cuando el documento se
   firma, se actualiza la columna `Status` de `PendingSignature` a `Signed`.

2. **El Archivo Físico (PDF / Firma Biométrica) → Object Storage Externo:**
   Los documentos firmados en PDF o capturas biométricas nunca se almacenan dentro de SQL Server (ni en columnas `VARBINARY` ni codificados en Base64 dentro del JSON) para evitar degradación del buffer
   pool y crecimiento descontrolado del almacenamiento transaccional. El binario se aloja en Object Storage (Azure Blob Storage / Amazon S3) y el `PayloadJson` conserva únicamente la referencia unívoca
   (`file_id`, `storage_url`).

3. **El Detalle Clínico del Acto → Columna `PayloadJson`:**
   Los datos clínicos propios del formulario (riesgos explicados al paciente, código de procedimiento quirúrgico, manifestaciones clínicas, escalas o motivos de modificación) se estructuran dentro del
   `PayloadJson`, quedando vinculados al contexto mediante las columnas relacionales `ExpedientId` y `EpisodeId`.

---

## 6. Ubicación y Organización de Archivos en la Solución (Arquitectura Multiproyecto)

Para entornos con múltiples módulos o microservicios clínicos (Alergias, Urgencias, Farmacia, Quirófano), el EMCI define una arquitectura distribuida con un núcleo compartido:

```text
📦 Organizacion.Salud.Solution/
 ┣ 📂 1. Core / Librerías Compartidas (EMCI Shared Kernel)
 ┃  ┗ 📜 Emci.Core/
 ┃     ┣ 📂 Entities/
 ┃     ┃  ┗ 📜 ClinicalDocumentRecord.cs      <-- (Contenedor Universal Contextual)
 ┃     ┣ 📂 Persistence/
 ┃     ┃  ┗ 📜 ClinicalDocumentConfiguration.cs <-- (Mapeo EF Core e Índices EMCI)
 ┃     ┗ 📂 Interfaces/
 ┃
 ┣ 📂 2. Módulos Clínicos / Microservicios Específicos
 ┃  ┣ 📜 Emci.ModuloAlergias.Api/              <-- (Módulo 1: AiR)
 ┃  ┃  ┣ 📂 Entities/
 ┃  ┃  ┃  ┗ 📜 PatientAllergy.cs               <-- (Entidad Transversal Activa)
 ┃  ┃  ┣ 📂 Models/
 ┃  ┃  ┃  ┗ 📜 AllergyCompositionPayloadDto.cs <-- (DTO del Payload Contextual)
 ┃  ┃  ┣ 📂 Validators/
 ┃  ┃  ┃  ┗ 📜 AllergyPayloadValidator.cs      <-- (Reglas FluentValidation)
 ┃  ┃  ┗ 📂 Controllers/
 ┃  ┃     ┗ 📜 AllergiesController.cs
 ┃  ┃
 ┃  ┗ 📜 Emci.ModuloConsentimientos.Api/       <-- (Módulo 2: Consentimientos)
 ┃     ┣ 📂 Models/
 ┃     ┃  ┗ 📜 InformedConsentPayloadDto.cs    <-- (DTO específico de Consentimientos)
 ┃     ┣ 📂 Validators/
 ┃     ┃  ┗ 📜 InformedConsentValidator.cs
 ┃     ┗ 📂 Controllers/
```

- **Librería Compartida (`Emci.Core`):** Define `ClinicalDocumentRecord`, sus configuraciones de índices en EF Core y contratos transversales. Cualquier evolución del contenedor base se propaga de forma
centralizada.
- **Proyectos de Dominio Clínico (`Emci.Modulo*.Api`):** Contienen sus propias entidades transversales (si su dominio requiere una proyección activa como `PatientAllergy`), sus DTOs de payload,
validadores `FluentValidation` y endpoints de aplicación.

### Diagrama de Arquitectura .NET bajo Estándar EMCI

```text
                 ┌────────────────────────────────────────┐
                 │          EXPEDIENT (Maestro Pura)      │
                 │          - ExpedientId (DNI / HC)      │
                 └───────────────────┬────────────────────┘
          ┌──────────────────────────┴──────────────────────────┐
          ▼                                                     ▼
┌─────────────────────────────────────────┐         ┌─────────────────────────┐
│     MÓDULO TRANSVERSAL (AiR Alergias)   │         │    MÓDULOS CLÍNICOS     │
│ - PatientAllergies (Resumen Activo)     │         │    (Urgencias, CCEE)    │
│ - EpisodeId: NULL (Global de paciente)  │         └────────────┬────────────┘
└─────────────────────────────────────────>                      │
                                                                 ▼
                                                    ┌─────────────────────────┐
                                                    │        EPISODIO         │
                                                    │   (EpisodeId: GUID)     │
                                                    └────────────┬────────────┘
                                                                 │
                                                                 │ Genera documento clínico contextual
                                                                 ▼
                                                    ┌─────────────────────────┐
                                                    │ CLINICALDOCUMENTRECORDS │
                                                    │ - Contenedor Universal  │
                                                    │ - TemplateCode (Ci, etc)│
                                                    │ - PayloadJson dinámico  │
                                                    └─────────────────────────┘

### Conceptos Fundamentales

1. **Expedient (`Patients` / `Expedient`):** Es la entidad maestra pura de la que nace toda la información del sistema. Se identifica de forma única mediante el `ExpedientId` (DNI, Historia Clínica, etc.).
2. **Episodio (`EpisodeId`):** Representa un acto asistencial acotado en el tiempo (un ingreso hospitalario, una consulta externa, una atención en urgencias o una intervención quirúrgica). Se identifica mediante un `GUID`.
3. **Módulos Transversales (Ej. Módulo AiR - Alergias):** Gestionan información clínica crónica o permanente del paciente. Su ámbito es global y no están atados a un episodio concreto (`EpisodeId = NULL`).
4. **Módulos Contextuales (Ej. Consentimientos Informados, Triaje, Quirófano):** Gestionan actos asistenciales específicos asociados a un episodio obligatorio.
5. **Contenedor Universal (`ClinicalDocumentRecords` - CDR):** Es la tabla universal donde convergen y se almacenan todos los documentos clínicos formalizados mediante `PayloadJson` y un `TemplateCode` maestro.

## 7. Casos de Uso de Persistencia

1. **Registro y Composición de Alergias (`AiR`):** Cuando el facultativo registra o actualiza una alergia durante la asistencia, se actualiza el resumen transversal activo del paciente en
`PatientAllergies` (vinculado a su `ExpedientId`) y se persiste la composición documental/auditoría del acto en `ClinicalDocumentRecords` con su `ExpedientId`, `EpisodeId`, `Status = "Active"` y
`TemplateCode = "EMCI_ALLERGY_COMPOSITION_V1"`.
2. **Consentimiento Informado Quirúrgico:** Al emitir un consentimiento para una artroscopia, se inserta un registro en `ClinicalDocumentRecords` con el `ExpedientId` del paciente, el `EpisodeId` del
episodio quirúrgico, `TemplateCode = "EMCI_INFORMED_CONSENT_V1"`, `Status = "PendingSignature"` y los datos clínicos junto a la referencia del PDF en `PayloadJson`.

---

## 8. Estructuras de `PayloadJson`

Dado que `ExpedientId` y `EpisodeId` forman parte de las columnas relacionales del contenedor `ClinicalDocumentRecords`, **no se duplican dentro del cuerpo JSON**, manteniendo el payload limpio y
centrado únicamente en los atributos clínicos de la plantilla.

### A. Payload de Composición de Alergias (`EMCI_ALLERGY_COMPOSITION_V1`)

```json
{
  "composition_uid": "87284370-2D4B-4e3d-A3F3-F303D2F4F34B::catsalut.cat::2",
  "composition_version": 2,
  "clinical_summary": "Paciente con historial de alergias medicamentosas.",
  "audit_metadata": {
    "catsalut_action": "MODIFICACIO",
    "modification_reason": "Actualización de severidad tras interconsulta",
    "additional_comments": "Confirmado mediante prueba cutánea"
  },
  "allergies": [
    {
      "substance_code": "SNOMED_373268000",
      "causal_agent": "Penicilina",
      "allergy_type": "Medicamento",
      "severity": "Severa",
      "manifestations": [
        {
          "code": "39579001",
          "description": "Anafilaxia",
          "oid": "2.16.840.1.113883.6.96"
        }
      ],
      "onset_date": "2020-05-12"
    }
  ]
}
```

### B. Payload de Consentimiento Informado (`EMCI_INFORMED_CONSENT_V1`)

```json
{
  "intervention_details": {
    "intervention_code": "CIR_ARTROSCOPIA_RODILLA",
    "intervention_name": "Artroscopia quirúrgica de rodilla derecha",
    "explained_risks": [
      "Infección articular",
      "Trombosis venosa profunda",
      "Rigidez postoperatoria"
    ]
  },
  "consent_given": true,
  "informed_at": "2026-06-06T09:30:00Z",
  "document_storage": {
    "file_id": "pdf-uuid-987654",
    "file_name": "consentimiento_rodilla_signed.pdf",
    "storage_url": "/api/storage/files/pdf-uuid-987654"
  }
}
```

---

## 9. Registro en Base de Datos (SQL)

La tabla transaccional `ClinicalDocumentRecords` conserva una estructura relacional uniforme e indexada:

| Id | ExpedientId | EpisodeId | TemplateCode | Status | RecordedAt | PayloadJson |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| `doc-01` | `exp-001` | `epi-45` | `EMCI_ALLERGY_COMPOSITION_V1` | `Active` | `2026-01-10` | `{"composition_uid": "...", "allergies": [...]}` |
| `doc-02` | `exp-001` | `epi-99` | `EMCI_INFORMED_CONSENT_V1` | `Signed` | `2026-06-06` | `{"intervention_details": {...}, "document_storage": {...}}` |

---
## 10. Estrategia de Persistencia: Espejo Ligero vs. CDR

Para resolver el dilema entre rendimiento de lectura y auditoría completa, EMCI divide la persistencia de datos transversales en dos capas:

- Tabla Ligera (PatientAllergies): Almacena únicamente el estado activo actual del paciente con columnas relacionales puras y planas (CausalAgent, Severity, IsActive). Permite consultas en milisegundos para mostrar alertas de alergias en la cabecera del sistema o validar prescripciones farmacéuticas.

- Tabla Universal (ClinicalDocumentRecords): Almacena la composición documental completa de cualquier cambio u orden de alergia mediante un PayloadJson inalterable, garantizando trazabilidad y validez legal (EpisodeId = NULL).

## 11. Estrategias de Acceso y Consulta sobre Datos JSON

Para recuperar o filtrar información almacenada en columnas JSON dentro de la arquitectura EMCI, se definen dos patrones según el caso de uso:

### Opción 1: Deserialización del `PayloadJson` a DTOs Tipados (Lectura de Documento por ID)

Cuando un servicio de aplicación necesita procesar, validar o presentar un documento específico de `ClinicalDocumentRecords`, recupera la fila relacional por su clave primaria y deserializa el
`PayloadJson` hacia el DTO asociado a su `TemplateCode`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Emci.ModuloAlergias.Api.Models;

[HttpGet("{id:guid}")]
public async Task<IActionResult> GetClinicalDocument(Guid id, CancellationToken ct)
{
    var record = await _context.ClinicalDocumentRecords
        .AsNoTracking()
        .FirstOrDefaultAsync(r => r.Id == id, ct);

    if (record is null) return NotFound();

    if (record.TemplateCode == "EMCI_ALLERGY_COMPOSITION_V1")
    {
        var payload = JsonSerializer.Deserialize<AllergyCompositionPayloadDto>(record.PayloadJson);

        return Ok(new
        {
            record.Id,
            record.ExpedientId,
            record.EpisodeId,
            record.Status,
            record.RecordedAt,
            Payload = payload
        });
    }

    return BadRequest(new { Message = $"Plantilla '{record.TemplateCode}' no soportada por este módulo." });
}
```

### Opción 2: Consultas Nativas en BD mediante Colecciones JSON Mapeadas en EF Core 8+ (`ToJson`)

Cuando se requiere filtrar en base de datos evaluando **todos los elementos de una colección JSON** (por ejemplo, buscar todas las alergias de un expediente donde cualquiera de sus manifestaciones o
elementos anidados cumpla un criterio), se utilizan colecciones mapeadas con `OwnsMany(...).ToJson()`.

EF Core traduce las expresiones LINQ `.Any(...)` sobre colecciones JSON directamente a `OPENJSON` en SQL Server, evaluando la totalidad del array sin traer los registros a memoria:

```csharp
// 1. Configuración en OnModelCreating (Colecciones mapeadas como JSON en EF Core 8+)
modelBuilder.Entity<ClinicalDocumentRecord>(entity =>
{
    entity.ToTable("ClinicalDocumentRecords", "dbo");
    entity.HasKey(e => e.Id);
    // Alternativamente, sobre PatientAllergies o sobre un Owned Type del Payload:
});

modelBuilder.Entity<PatientAllergy>(entity =>
{
    entity.ToTable("PatientAllergies", "dbo");
    entity.HasKey(e => e.Id);
    entity.Property(e => e.ExpedientId).IsRequired().HasMaxLength(50);

    // Mapeo nativo a columnas JSON sin tablas hijas adicionales
    entity.OwnsMany(e => e.Manifestations, b => b.ToJson());
    entity.OwnsMany(e => e.Associations, b => b.ToJson());
    entity.OwnsMany(e => e.CausalAgents, b => b.ToJson());
});

// 2. Consulta LINQ evaluando TODOS los elementos del array JSON en SQL Server (OPENJSON)
var severeAllergiesWithAnaphylaxis = await _context.PatientAllergies
    .AsNoTracking()
    .Where(a => a.ExpedientId == expedientId &&
                a.IsActive &&
                a.Severity == "Severa" &&
                // Evalúa todas las manifestaciones del array JSON, no solo la posición [0]
                a.Manifestations.Any(m => m.ManifestationCode == "39579001"))
    .ToListAsync(ct);
```

---

## 11. Extensibilidad: Incorporación de Nuevos Atributos Clínicos

Para incorporar un nuevo campo clínico en una plantilla existente (por ejemplo, añadir `"verification_method"` al registro de alergias):

1. **Actualizar el Contrato DTO (`AllergyItemDto.cs`):** Añadir la propiedad `VerificationMethod`.
2. **Actualizar el Validador (`AllergyPayloadValidator.cs`):** Definir las reglas de negocio en `FluentValidation`.
3. **Despliegue directo sin DDL:** No se requiere migración de esquema SQL en `ClinicalDocumentRecords`.

---

## 12. Visualización y Gobierno de Plantillas (`EMCI Template Explorer`)

Dado que el contrato semántico de cada formulario clínico reside en los DTOs tipados asociados a un `TemplateCode` y sus reglas en `FluentValidation`, la arquitectura EMCI permite exponer un catálogo
interactivo de plantillas (**EMCI Template Explorer**, análogo a OpenAPI/Swagger).

Mediante reflexión al inicio de la aplicación, un endpoint de metadatos inspecciona los DTOs registrados por `TemplateCode`, generando automáticamente el JSON Schema, las restricciones de validación y
ejemplos de payload para facilitar la integración con el frontend clínico y otros sistemas hospitalarios.
