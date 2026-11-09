# EMCI (Estándar de Modelado Clínico Interno) - Documentación Técnica y Modelo .NET

Este documento detalla la arquitectura híbrida basada en los principios semánticos de openEHR adaptada para bases de datos relacionales internas en .NET (ASP.NET Core + EF Core + FluentValidation).

---

## 1. ¿Qué es el EMCI?
El **Estándar de Modelado Clínico Interno (EMCI)** es un enfoque pragmático para sistemas clínicos. En lugar de crear un motor hipercomplejo de arquetipos y diccionarios de metadatos dinámicos, el EMCI separa:
- **El Núcleo Relacional Estable:** Identificadores universales (`PatientId`, `EpisodeId`, estado del workflow `Status`, auditoría y metadatos del documento).
- **El Contenido Clínico Dinámico (`PayloadJson`):** Estructuras flexibles en formato JSON que pueden evolucionar sin modificar el esquema de la base de datos (evitando migraciones continuas de EF Core).

---

## 2. Ventajas del Modelo
1. **Cero Migraciones por Cambios de Formularios:** Si el personal médico requiere nuevos campos en una escala de alergias, triaje o anamnesis, no se altera la estructura relacional de la BD; solo se actualiza el DTO y el validador en .NET.
2. **Coherencia Arquitectónica:** Todos los módulos clínicos de la organización (Cardiología, Urgencias, Farmacia, etc.) almacenan la información variable bajo el mismo patrón estandarizado (`ClinicalDocumentRecord`).
3. **Validación Robusta en Código:** Uso de **FluentValidation** para garantizar la integridad clínica (rangos, catálogos permitidos, obligatoriedad) antes de persistir los datos.
4. **Alto Rendimiento en Consultas Relacionales:** Facilita índices en campos críticos (`PatientId`, `EpisodeId`, `Status`, `TemplateCode`) combinados con la flexibilidad documental.

---

## 3. Vinculación con Expedientes, Episodios e Intervenciones
Los datos en salud siguen una jerarquía contextual:
- **Datos Transversales (Alergias, Antecedentes):** Cuelgan directamente del `PatientId` y su `EpisodeId` es `NULL` (aplican globalmente a toda la vida del paciente).
- **Datos Contextuales (Consentimientos Informados, Notas Quirúrgicas):** Cuelgan del `PatientId` y requieren obligatoriamente un `EpisodeId` (asociados a un expediente, ingreso o intervención específica).

### Regla de Oro EMCI:
> Las relaciones estructurales de contexto (`PatientId`, `EpisodeId`, `Status`) van en el **núcleo relacional SQL** para búsquedas rápidas. El detalle del acto clínico va dentro del **`PayloadJson`**.

---

## 4. Gestión de Documentos Pesados, Workflows, Auditoría y Catálogos

| Necesidad Operativa | Estrategia EMCI | Explicación |
| :--- | :--- | :--- |
| **PDFs y Binarios (Base64)** | Storage Externo (Azure Blob / S3) | **Nunca guardar Base64 pesados en el JSON**. Se guarda la URL o ID de archivo dentro del `PayloadJson` y el archivo físico va al Storage. |
| **Rastreo de Estados / Workflows** | Columna `Status` en SQL | El estado (`Draft`, `PendingSignature`, `Signed`, `Revoked`) vive en la tabla relacional para permitir filtros y bandejas de trabajo rápidas sin parsear JSON. |
| **Auditoría de Cambios** | Tabla Relacional `AuditLogs` | Integración mediante `DbContext` (ej. `Audit.NET`) para registrar de forma independiente quién creó, modificó o eliminó un documento. |
| **Catálogos Maestros (Agentes Causales)** | Tablas Relacionales de Referencia | Catálogos normalizados (ej. SNOMED, CIE-10) en SQL para alimentar desplegables en UI. Los códigos seleccionados se inyectan en el JSON. |
| **Sincronización (Offline/Sync)** | Patrón Outbox (`SyncOutboxQueue`) | Una cola de eventos relacional procesa los registros y envía el `PayloadJson` a sistemas externos/hospitales. |

---

## 5. Ubicación y Organización de Archivos en la Solución (Arquitectura Multiproyecto)

Cuando se desarrollan múltiples módulos o microservicios (Urgencias, Farmacia, Cardiología), el EMCI propone una estructura hibridada:

```text
📦 MiEmpresa.Clinica.Solution/
 ┣ 📂 1. Core / Librerías Compartidas (EMCI Kernel)
 ┃  ┗ 📜 Emci.Core/
 ┃     ┣ 📂 Models/
 ┃     ┃  ┗ 📜 ClinicalDocumentRecord.cs  <-- (La entidad relacional universal)
 ┃     ┗ 📂 Interfaces/
 ┃
 ┣ 📂 2. Microservicios / Aplicaciones Específicas
 ┃  ┣ 📜 Emci.ModuloAlergias.Api/          <-- (Proyecto 1)
 ┃  ┃  ┣ 📂 Models/
 ┃  ┃  ┃  ┗ 📜 AllergyPayloadDto.cs       <-- (DTOs específicos de Alergias)
 ┃  ┃  ┣ 📂 Validators/
 ┃  ┃  ┃  ┗ 📜 AllergyPayloadValidator.cs <-- (FluentValidation de Alergias)
 ┃  ┃  ┗ 📂 Controllers/
 ┃  ┃     ┗ 📜 AllergiesController.cs
 ┃  ┃
 ┃  ┗ 📜 Emci.ModuloConsentimientos.Api/   <-- (Proyecto 2)
 ┃     ┣ 📂 Models/
 ┃     ┃  ┗ 📜 ConsentPayloadDto.cs       <-- (DTOs específicos de Consentimientos)
 ┃     ┣ 📂 Validators/
 ┃     ┗ 📂 Controllers/
```

- **Librería Compartida (`Emci.Core`):** Contiene `ClinicalDocumentRecord` e interfaces reutilizables. Al ser común a todas las aplicaciones, cualquier cambio estructural en el núcleo SQL se propaga centralizadamente.
- **Proyectos Específicos (`Emci.Modulo.Api`):** Contienen sus propios DTOs, validadores (`FluentValidation`) y controladores HTTP. Esto permite que cada dominio clínico evolucione sin romper al resto.

---

## 6. Ejemplo Escrito de Uso
1. **Alergias:** Un médico registra alergias del paciente. El payload viaja con `EpisodeId = null` y `TemplateCode = "CLIN_REGISTRO_ALERGIAS_V1"`.
2. **Consentimiento Informado:** Se genera un consentimiento para una artroscopia. Se asigna el `EpisodeId` de la hospitalización, el `Status = "PendingSignature"` y en el `PayloadJson` la referencia al archivo PDF en el Blob Storage.

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

### B. Payload de Consentimiento Informado (Dato de Episodio e Intervención)
```json
{
  "patient_id": "d4e2c1a8-8b45-4211-9f12-34567890abcd",
  "episode_id": "f8c2e1b3-4a12-4c22-8b11-7890123456ef",
  "intervention_details": {
    "intervention_code": "CIR_ARTROSCOPIA_RODILLA",
    "intervention_name": "Artroscopia quirúrgica de rodilla derecha"
  },
  "consent_given": true,
  "document_storage": {
    "file_id": "pdf-uuid-987654",
    "file_name": "consentimiento_rodilla_signed.pdf",
    "storage_url": "/api/storage/files/pdf-uuid-987654"
  }
}
```

---

## 8. Registro en Base de Datos (SQL)

La tabla transaccional `ClinicalDocumentRecords` mantiene una estructura uniforme y relacional:

| Id | PatientId | EpisodeId | TemplateCode | Status | RecordedAt | PayloadJson |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| `doc-01` | `pat-001` | `NULL` | `CLIN_REGISTRO_ALERGIAS_V1` | `Active` | `2026-01-10` | `{"allergies": [...]}` |
| `doc-02` | `pat-001` | `epi-99` | `CLIN_CONSENTIMIENTO_INFORMADO_V1` | `Signed` | `2026-06-06` | `{"document_storage": {...}}` |

---

## 9. Extensibilidad: Cómo añadir un nuevo atributo
Para añadir un campo nuevo (ej. `"verification_method"` en alergias):
1. **Actualizar el DTO (`AllergyItemDto.cs`):** Añadir la propiedad `VerificationMethod`.
2. **Actualizar el Validador (`AllergyPayloadValidator.cs`):** Incluir las reglas de `FluentValidation`.
3. **No se requiere migración de BD SQL.**