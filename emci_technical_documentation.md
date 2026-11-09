# EMCI (Estándar de Modelado Clínico Interno) - Documentación Técnica y Arquitectura .NET

Este documento establece la guía técnica completa del **Estándar de Modelado Clínico Interno (EMCI)**, una arquitectura de datos clínica orientada a producción en .NET (ASP.NET Core + EF Core + FluentValidation) con soporte para bases de datos relacionales (SQL Server / PostgreSQL).

---

## 1. Visión General y Alcance

El **EMCI** resuelve la tensión histórica en informática médica entre la **rigidez de esquemas relacionales tradicionales** y la **hipercomplejidad semántica de estándares como openEHR**.

### Separación de Responsabilidades:
1. **Núcleo Relacional Estable:** Columnas SQL tipadas e indizadas para metadatos clave, trazabilidad, identidad del paciente, contexto asistencial y gestión de workflows (`PatientId`, `EpisodeId`, `Status`, `TemplateCode`, `Version`, fechas).
2. **Contenido Clínico Dinámico (`PayloadJson`):** Estructuras flexibles en formato JSON serializado para capturar el detalle de los formularios médicos. Se validan estrictamente en la capa de aplicación .NET antes de persistir.

---

## 2. Ventajas Arquitectónicas

- **Cero Migraciones SQL por Cambios de Formularios:** Modificar o extender campos de un triaje, escala médica o registro de alergias no altera la tabla SQL. Solo se actualiza el DTO y el validador en .NET.
- **Rendimiento Relacional Nativo:** Consultas por paciente, expediente y estado de firma se ejecutan mediante índices B-Tree relacionales estándar.
- **Integridad Garantizada por Código:** Uso de **FluentValidation** para asegurar que los JSON almacenados cumplan con rangos, catálogos permitidos y tipos requeridos.
- **Estandarización Organizacional:** Todos los dominios y microservicios comparten la misma entidad raíz (`ClinicalDocumentRecord`).

---

## 3. Clasificación Contextual de los Datos Clínicos

Los datos en salud siguen una jerarquía contextual que determina su relación con los expedientes:

```
                  ┌────────────────────────┐
                  │    PACIENTE (DNI/HC)   │
                  └───────────┬────────────┘
                              │
         ┌────────────────────┴────────────────────┐
         ▼                                         ▼
┌─────────────────────────────────┐   ┌─────────────────────────┐
│ Datos Transversales (Globales)  │   │ Expediente / Episodio   │
│ - EpisodeId: NULL               │   │ (Ingreso, Urgencia)     │
│ - Ej: Alergias, Grupo Sanguíneo │   └────────────┬────────────┘
└─────────────────────────────────┘                │
                                                   ▼
                                      ┌─────────────────────────┐
                                      │  Datos Contextuales     │
                                      │  - EpisodeId: GUID      │
                                      │  - Ej: Consentimientos  │
                                      └─────────────────────────┘
```

- **Datos Transversales (Globales):** Información permanente a lo largo de la vida del paciente (ej. Alergias, Antecedentes familiares). El campo `EpisodeId` se registra como `NULL`.
- **Datos Contextuales (Episódicos):** Información vinculada a un acto, ingreso o intervención quirúrgica específica (ej. Consentimiento Informado, Nota Quirúrgica). Requieren obligatoriamente un `EpisodeId`.

---

## 4. Gestión Operativa Completa

### A. Workflows y Estados (`Status`)
El estado (`Draft`, `PendingSignature`, `Signed`, `Active`, `Revoked`) **reside siempre en la columna relacional SQL**.
- *Razón:* Permite construir bandejas de trabajo rápidas (`SELECT * FROM ClinicalDocumentRecords WHERE Status = 'PendingSignature'`) sin requerir parseo ni inspección de JSONs.

### B. Gestión de Archivos Pesados (PDFs / Binarios Base64)
- **Regla:** Queda prohibido almacenar cadenas Base64 o binarios dentro de `PayloadJson` o la base de datos SQL para evitar degradación de memoria e I/O.
- **Solución:** Los archivos físicos se suben a un **Object Storage** (Azure Blob Storage / AWS S3) y en el `PayloadJson` solo se almacena el metadata y la referencia (`storage_url` / `file_id`).

### C. Catálogos Normalizados (Agentes Causales / Diagnósticos)
Los elementos seleccionables (ej. lista de fármacos o códigos SNOMED/CIE-10) provienen de tablas relacionales maestras para alimentar las desplegables de UI. En el JSON se inyecta el código estandarizado (`allergen_code`) y la descripción legible.

### D. Trazabilidad y Auditoría Legales (`AuditLogs`)
La auditoría **no se incluye dentro del JSON**. Vive en una tabla relacional independiente `AuditLogs` que registra los cambios de forma automática mediante la sobreescritura de `DbContext.SaveChangesAsync()`:

```csharp
public class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TableName { get; set; } = "ClinicalDocumentRecords";
    public Guid RecordId { get; set; }
    public string Action { get; set; } = string.Empty; // Inserted, Updated, Deleted
    public string AuthorId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string ChangesSummary { get; set; } = string.Empty;
}
```

---

## 5. Pilares Avanzados de Producción

### A. Versionado de Plantillas (`TemplateCode Evolution`)
Cuando la estructura clínica evoluciona (ej. de `CLIN_REGISTRO_ALERGIAS_V1` a `V2`), se mantiene la compatibilidad histórica mediante el patrón Factory/Estrategia en .NET:
- **Nuevos Registros:** Se guardan con el código `V2` y su correspondiente DTO/Validador.
- **Registros Antiguos:** Al leer registros con `V1`, el servicio aplica un mapeador de migración en memoria (DTO Upgrader) para exponer una respuesta unificada a la API sin alterar los JSONs históricos en la BD.

### B. Consultas e Indexación sobre JSON en SQL
Para realizar filtros de alto rendimiento dentro del contenido JSON sin hacer recorridos de tabla completos (*Table Scans*):

- **En SQL Server (Computed Columns):**
  ```sql
  ALTER TABLE ClinicalDocumentRecords 
  ADD PrimaryAllergen AS JSON_VALUE(PayloadJson, '$.allergies[0].allergen_code');
  
  CREATE INDEX IX_ClinicalDocumentRecords_PrimaryAllergen 
  ON ClinicalDocumentRecords(PrimaryAllergen);
  ```

- **En PostgreSQL (Índices GIN sobre JSONB):**
  ```sql
  CREATE INDEX idx_clinical_payload_gin 
  ON "ClinicalDocumentRecords" USING GIN (("PayloadJson"::jsonb));
  ```

### C. Seguridad, Cifrado y Privacidad (RGPD / HIPAA)
- **Cifrado en Reposo:** Los campos sensibles dentro del JSON (ej. datos diagnósticos o datos de salud mental) son cifrados a nivel de aplicación en .NET antes de serializarse si el entorno lo exige.
- **Anonimización/Enmascaramiento:** La API aplica DTOs de proyección pública para omitir o enmascarar datos clínicos en función de los roles de usuario (ej. personal administrativo vs. equipo sanitario).

### D. Concurrencia Optimista (Optimistic Locking)
Para evitar sobreescrituras accidentales cuando múltiples profesionales modifican simultáneamente un documento o estado:
- Se añade un campo de versión/timestamp relacional a la entidad `ClinicalDocumentRecord`.
- EF Core valida en el `UPDATE` que la versión en base de datos coincida con la leída originalmente, lanzando una `DbUpdateConcurrencyException` en caso de conflicto.

---

## 6. Arquitectura de Solución Multiproyecto (.NET)

Estructura hibridada para soluciones con múltiples dominios o microservicios:

```text
📦 MiEmpresa.Clinica.Solution/
 ┣ 📂 1. Core / Librerías Compartidas (EMCI Kernel)
 ┃  ┗ 📜 Emci.Core/
 ┃     ┣ 📂 Models/
 ┃     ┃  ┗ 📜 ClinicalDocumentRecord.cs  <-- (Entidad relacional universal)
 ┃     ┣ 📂 Audit/
 ┃     ┃  ┗ 📜 AuditLog.cs
 ┃     ┗ 📂 Interfaces/
 ┃
 ┣ 📂 2. Microservicios / Aplicaciones Específicas
 ┃  ┣ 📜 Emci.ModuloAlergias.Api/          <-- (Módulo Alergias)
 ┃  ┃  ┣ 📂 Models/
 ┃  ┃  ┃  ┗ 📜 AllergyPayloadDto.cs       <-- (DTOs específicos)
 ┃  ┃  ┣ 📂 Validators/
 ┃  ┃  ┃  ┗ 📜 AllergyPayloadValidator.cs <-- (Reglas FluentValidation)
 ┃  ┃  ┗ 📂 Controllers/
 ┃  ┃     ┗ 📜 AllergiesController.cs
 ┃  ┃
 ┃  ┗ 📜 Emci.ModuloConsentimientos.Api/   <-- (Módulo Consentimientos)
 ┃     ┣ 📂 Models/
 ┃     ┃  ┗ 📜 ConsentPayloadDto.cs
 ┃     ┣ 📂 Validators/
 ┃     ┗ 📂 Controllers/
```

- **Librería Compartida (`Emci.Core`):** Contiene `ClinicalDocumentRecord`, la gestión de auditoría `AuditLog` e interfaces comunes.
- **Proyectos Específicos (`Emci.Modulo.Api`):** Contienen sus propios DTOs, validadores y controladores, permitiendo que cada dominio evolucione independientemente.

---

## 7. Estructuras de Payload JSON

### A. Payload de Alergias (Dato Transversal)
```json
{
  "patient_id": "d4e2c1a8-8b45-4211-9f12-34567890abcd",
  "clinical_summary": "Paciente con historial de alergias medicamentosas.",
  "allergies": [
    {
      "allergen_code": "SNOMED_373268000",
      "allergen": "Penicilina",
      "allergy_type": "Medicamento",
      "severity": "Severa",
      "reaction": "Anafilaxia, urticaria aguda",
      "status": "Activa",
      "onset_date": "2020-05-12"
    }
  ]
}
```

### B. Payload de Consentimiento Informado (Dato Contextual de Episodio)
```json
{
  "patient_id": "d4e2c1a8-8b45-4211-9f12-34567890abcd",
  "episode_id": "f8c2e1b3-4a12-4c22-8b11-7890123456ef",
  "intervention_details": {
    "intervention_code": "CIR_ARTROSCOPIA_RODILLA",
    "intervention_name": "Artroscopia quirúrgica de rodilla derecha"
  },
  "consent_given": true,
  "risks_explained": [
    "Infección nosocomial",
    "Trombosis venosa profunda"
  ],
  "document_storage": {
    "file_id": "pdf-uuid-987654",
    "file_name": "consentimiento_rodilla_signed.pdf",
    "storage_url": "/api/storage/files/pdf-uuid-987654"
  }
}
```

---

## 8. Registro en Base de Datos (SQL)

Muestra de la convergencia relacional con el contenido dinámico en `ClinicalDocumentRecords`:

| Id | PatientId | EpisodeId | TemplateCode | Status | RecordedAt | PayloadJson |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| `doc-01` | `pat-001` | `NULL` | `CLIN_REGISTRO_ALERGIAS_V1` | `Active` | `2026-01-10 10:00` | `{"allergies": [...]}` |
| `doc-02` | `pat-001` | `epi-99` | `CLIN_CONSENTIMIENTO_INFORMADO_V1` | `Signed` | `2026-06-06 11:30` | `{"document_storage": {...}}` |

---

## 9. Guía de Extensibilidad

Para añadir un nuevo atributo a un formulario (ejemplo: `"verification_method"` en alergias):

1. **Modificar el DTO C# (`AllergyItemDto.cs`):**
   ```csharp
   public string VerificationMethod { get; set; } = string.Empty;
   ```
2. **Actualizar el Validador (`AllergyPayloadValidator.cs`):**
   ```csharp
   RuleFor(x => x.VerificationMethod).NotEmpty();
   ```
3. **Resultado:** Se ejecuta sin requerir ninguna migración de base de datos SQL.