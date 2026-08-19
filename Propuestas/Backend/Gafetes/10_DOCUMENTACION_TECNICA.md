# Documentación Técnica: Sincronización de Gafetes de Casco

## ESTADO: PROPUESTA SIN ACTIVAR

**Fecha**: 2026-07-20  
**Estado**: Preparado, no desplegado  
**Próximos pasos**: Revisión y aprobación antes de despliegue en Hostinger

---

## Tabla de Contenidos

1. [Visión General](#visión-general)
2. [Arquitectura](#arquitectura)
3. [Componentes](#componentes)
4. [Especificaciones](#especificaciones)
5. [Flujo de Sincronización](#flujo-de-sincronización)
6. [Persistencia](#persistencia)
7. [Seguridad](#seguridad)
8. [Pruebas](#pruebas)
9. [Despliegue](#despliegue)
10. [Troubleshooting](#troubleshooting)

---

## Visión General

### Objetivo

Sincronizar gafetes de Casco desde el cliente local (ControlTaxiDesktop.Tools) hacia un backend centralizado sin modificar:
- ❌ Registros de taxis
- ❌ Tarifas
- ❌ Pagos/Comisiones
- ❌ Viajes/Dejadas
- ❌ Plaza 28

### Alcance

**Proyecto**: Control Taxi - Casco  
**Ámbito**: Solo gafetes (badges)  
**Sucursal**: CV (Casco Viejo)  
**Base de datos**: Hostinger (mktCasco)

### Restricciones

✓ **Idempotencia**: Mismo payload enviado 2 veces = sin duplicados  
✓ **UPSERT**: Clave única (branch_code, badge_id, cycle)  
✓ **Seguridad**: Autenticación Bearer Token + CORS  
✓ **Sin Plaza 28**: Protegido a nivel de código

---

## Arquitectura

```
Cliente Local (Desktop)
    |
    v
CascoBadgeSyncService (tools CLI)
    |
    +-- Genera payload JSON
    +-- Valida payload
    |
    v
CascoGafetesApiClient (propuesto)
    |
    v
POST /casco-api/api/gafetes/sync (backend)
    |
    +-- GafetesController (ASP.NET Core)
    |
    +-- GafetesValidator (validación)
    |
    +-- GafetesSyncService (negocio)
    |
    +-- DbConnectionFactory (datos)
    |
    v
SQL Server (Hostinger)
    |
    +-- casco_gafetes (tabla principal)
    +-- casco_gafetes_audit (auditoría)
```

---

## Componentes

### 1. DTOs (Contratos)

**Archivo**: `01_GafetesContracts.cs`

```csharp
// Request
SyncGafetesRequest
  - BranchCode: string (requerido: "CV")
  - Gafetes: List<GafeteDto> (array de gafetes)
  - Mkt2Gafetes: List<GafeteDto> (alternativa para compatibilidad)

GafeteDto
  - BadgeId: string (requerido, 1-50 chars)
  - Barcode: string (requerido, 1-50 chars)
  - Status: string (A, R, S)
  - Cycle: int (>= 1)
  - TaxistaId: int? (opcional)
  - TaxistaName: string? (opcional)
  - CreatedAt: string (ISO 8601)

// Response
SyncGafetesResponse
  - Success: bool
  - BranchCode: string
  - Received: int (total gafetes)
  - Inserted: int
  - Updated: int
  - Unchanged: int
  - Errors: List<string>
  - ProcessedAt: DateTime
```

### 2. Modelo de Dominio

**Archivo**: `02_GafetesModels.cs`

```csharp
Gafete
  - Id: int (PK)
  - BranchCode: string
  - BadgeId: string
  - Barcode: string
  - Status: string (A|R|S)
  - Cycle: int
  - TaxistaId: int?
  - TaxistaName: string?
  - CreatedAt: DateTime
  - UpdatedAt: DateTime
  - IsActive: bool

GafeteStatus (constantes)
  - Activo = "A"
  - Retirado = "R"
  - Suspendido = "S"
```

### 3. Validador

**Archivo**: `03_GafetesValidator.cs`

Validaciones:
- ✓ BranchCode = "CV" (nunca "28")
- ✓ Gafetes no vacío (1-1000 items)
- ✓ BadgeId obligatorio (1-50 chars)
- ✓ Status válido (A, R, S)
- ✓ Cycle >= 1
- ✓ CreatedAt fecha válida (ISO 8601)
- ✓ Sin duplicados (branchCode + badgeId + cycle)

### 4. Servicio de Sincronización

**Archivo**: `04_GafetesSyncService.cs`

Responsabilidades:
- UPSERT por (branch_code, badge_id, cycle)
- Transacciones ACID
- Logging de operaciones
- Manejo de errores

Operaciones:
- **INSERT**: Gafete nuevo
- **UPDATE**: Status o datos cambiaron
- **UNCHANGED**: Sin cambios (idempotencia)

### 5. Controlador ASP.NET Core

**Archivo**: `05_GafetesController.cs`

Endpoint:
```
POST /api/gafetes/sync
Authorization: Bearer {token}
Content-Type: application/json
```

Respuestas:
- 200 OK: Sincronización exitosa
- 400 Bad Request: BranchCode inválido
- 401 Unauthorized: Sin autenticación
- 422 Unprocessable Entity: Payload inválido
- 500 Internal Server Error: Error servidor

---

## Especificaciones

### Validaciones por Campo

| Campo | Tipo | Requerido | Validación |
|-------|------|-----------|-----------|
| branchCode | string | Sí | = "CV", nunca "28" |
| BadgeId | string | Sí | 1-50 chars |
| Barcode | string | Sí | 1-50 chars |
| Status | string | Sí | "A", "R", o "S" |
| Cycle | int | Sí | >= 1 |
| TaxistaId | int | No | >= 0 o null |
| TaxistaName | string | No | <= 255 chars |
| CreatedAt | string | Sí | ISO 8601 válido |

### Códigos HTTP

| Código | Caso | Ejemplo |
|--------|------|---------|
| 200 | Éxito | `{"success": true, "inserted": 1}` |
| 400 | BranchCode != "CV" | `{"success": false, "error": "..."}` |
| 400 | Gafetes vacío | `{"success": false, "error": "..."}` |
| 401 | Falta token | `{"success": false, "error": "..."}` |
| 422 | Status inválido | `{"success": false, "error": "...", "errorIndex": 0}` |
| 422 | Fecha inválida | `{"success": false, "error": "..."}` |
| 422 | Duplicados | `{"success": false, "error": "..."}` |
| 500 | Error servidor | `{"success": false, "error": "..."}` |

---

## Flujo de Sincronización

### Paso 1: Cliente Local Prepara Payload

```
CascoBadgeSyncService.LoadPreviewAsync()
  |
  +-- Consulta BD local (mktCasco)
  +-- Obtiene gafetes pendientes
  +-- Construye payload JSON
  +-- Retorna CascoBadgeSyncPreview
```

### Paso 2: Cliente Local Valida

```
Payload → GafetesValidator.Validate()
  |
  +-- Checkea BranchCode
  +-- Checkea gafetes no vacío
  +-- Valida cada gafete
  +-- Detecta duplicados
  |
  v
ValidationResult (errores o OK)
```

### Paso 3: Cliente Local Envía

```
POST /api/gafetes/sync
  |
  v
GafetesController.SyncGafetes()
  |
  +-- Valida request
  +-- Llama GafetesSyncService
  |
  v
GafetesSyncService.SyncGafetesAsync()
  |
  +-- Abre transacción
  +-- Para cada gafete:
  |   +-- UpsertGafeteAsync()
  |       +-- GetExistingGafeteAsync()
  |       +-- Si no existe: INSERT
  |       +-- Si cambió: UPDATE
  |       +-- Si igual: OMITIR
  +-- Commit o Rollback
  |
  v
Retorna GafeteSyncResult
  (inserted, updated, unchanged, errors)
```

### Paso 4: Backend Responde

```
SyncGafetesResponse
  {
    "success": true,
    "branchCode": "CV",
    "received": 1,
    "inserted": 1,
    "updated": 0,
    "unchanged": 0,
    "errors": []
  }
```

---

## Persistencia

### Tabla: casco_gafetes

```sql
CREATE TABLE casco_gafetes (
    Id INT PRIMARY KEY IDENTITY,
    
    -- Claves de negocio
    BranchCode NVARCHAR(10) NOT NULL,
    BadgeId NVARCHAR(50) NOT NULL,
    Barcode NVARCHAR(50) NOT NULL,
    
    -- Estado
    Status NCHAR(1) NOT NULL,      -- A, R, S
    Cycle INT NOT NULL,             -- >= 1
    
    -- Taxista (opcional)
    TaxistaId INT NULL,
    TaxistaName NVARCHAR(255) NULL,
    
    -- Auditoría
    CreatedAt DATETIME2 NOT NULL,
    UpdatedAt DATETIME2 NOT NULL,
    IsActive BIT NOT NULL DEFAULT 1,
    SyncedAt DATETIME2 NULL,
    SyncSource NVARCHAR(50) NULL,   -- 'API', 'CascoSync', etc.
    
    -- Clave única por (branch, badge, cycle)
    CONSTRAINT UQ_CascoGafetes_BranchBadgeCycle 
        UNIQUE (BranchCode, BadgeId, Cycle)
);
```

### Índices

```sql
-- Búsqueda principal
IX_CascoGafetes_BranchCode (BranchCode, BadgeId, Cycle)

-- Filtrado por status
IX_CascoGafetes_Status (Status, BranchCode, IsActive)

-- Auditoría temporal
IX_CascoGafetes_UpdatedAt (UpdatedAt DESC)
```

### Tabla: casco_gafetes_audit

```sql
CREATE TABLE casco_gafetes_audit (
    AuditId BIGINT PRIMARY KEY IDENTITY,
    GafeteId INT NOT NULL,
    Operation NVARCHAR(10) NOT NULL,  -- INSERT, UPDATE, DELETE
    OldStatus NCHAR(1) NULL,
    OldTaxistaId INT NULL,
    OldTaxistaName NVARCHAR(255) NULL,
    NewStatus NCHAR(1) NULL,
    NewTaxistaId INT NULL,
    NewTaxistaName NVARCHAR(255) NULL,
    ChangedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    ChangedBy NVARCHAR(255) NULL,
    ChangeReason NVARCHAR(500) NULL
);
```

### Triggers

1. **TR_CascoGafetes_UpdatedAt**: Actualiza UpdatedAt automáticamente
2. **TR_CascoGafetes_Audit**: Registra cambios en tabla de auditoría

---

## Seguridad

### Autenticación

- Requerida: Bearer Token en header `Authorization`
- Validación: Mismo mecanismo que rutas actuales de Casco
- Token de ejemplo: `HokaTaxisSync2050` (del appsettings.json)

### Autorización

- Política: `CascoApiAccess`
- Permisos: `sync:gafetes` (si se implementa con claims)

### Protecciones

✓ **Plaza 28**: Rechazado en validación (400 Bad Request)  
✓ **Content-Type**: Solo `application/json`  
✓ **Tamaño de lote**: Máximo 1000 gafetes  
✓ **Rate limiting**: Implementado a nivel de API Gateway (opcional)  
✓ **CORS**: Permitir solo cliente local  
✓ **Logging**: Auditar todos los POST exitosos  

### Aislamiento

✓ **Nueva tabla**: casco_gafetes (independiente)  
✓ **Nuevos índices**: Sin afectar tablas existentes  
✓ **Sin modificación de datos**: No toca viajes, tarifas, pagos  
✓ **Transacciones aisladas**: Level = Serializable (ACID)

---

## Pruebas

### Pruebas Unitarias

**Archivo**: `07_GafetesTests.cs`

Casos de validación:
- ✓ Payload válido → OK
- ✓ BranchCode "28" → Error
- ✓ BranchCode inválido → Error
- ✓ Gafetes vacío → Error
- ✓ Status inválido → Error
- ✓ Cycle < 1 → Error
- ✓ CreatedAt inválido → Error
- ✓ BadgeId faltante → Error
- ✓ Duplicados en payload → Error
- ✓ Múltiples gafetes válidos → OK
- ✓ Formato mkt2_gafetes → OK
- ✓ Lote > 1000 → Error

### Pruebas de Integración (Manual)

#### Caso 1: Insert

```bash
curl -X POST http://localhost:5000/api/gafetes/sync \
  -H "Authorization: Bearer HokaTaxisSync2050" \
  -H "Content-Type: application/json" \
  -d '{
    "branchCode": "CV",
    "gafetes": [{
      "badgeId": "2121",
      "barcode": "2121",
      "status": "R",
      "cycle": 1,
      "taxistaId": null,
      "taxistaName": "",
      "createdAt": "2026-07-18T11:31:18"
    }]
  }'
```

**Respuesta esperada (200)**:
```json
{
  "success": true,
  "branchCode": "CV",
  "received": 1,
  "inserted": 1,
  "updated": 0,
  "unchanged": 0,
  "errors": []
}
```

#### Caso 2: Idempotencia (reenvío)

Mismo curl anterior, segunda vez.

**Respuesta esperada (200)**:
```json
{
  "success": true,
  "branchCode": "CV",
  "received": 1,
  "inserted": 0,
  "updated": 0,
  "unchanged": 1,
  "errors": []
}
```

#### Caso 3: Update (status cambió)

```bash
curl -X POST http://localhost:5000/api/gafetes/sync \
  -H "Authorization: Bearer HokaTaxisSync2050" \
  -H "Content-Type: application/json" \
  -d '{
    "branchCode": "CV",
    "gafetes": [{
      "badgeId": "2121",
      "barcode": "2121",
      "status": "A",  # Cambió de R a A
      "cycle": 1,
      "taxistaId": null,
      "taxistaName": "",
      "createdAt": "2026-07-18T11:31:18"
    }]
  }'
```

**Respuesta esperada (200)**:
```json
{
  "success": true,
  "branchCode": "CV",
  "received": 1,
  "inserted": 0,
  "updated": 1,
  "unchanged": 0,
  "errors": []
}
```

#### Caso 4: Error - Plaza 28

```bash
curl -X POST http://localhost:5000/api/gafetes/sync \
  -H "Authorization: Bearer HokaTaxisSync2050" \
  -H "Content-Type: application/json" \
  -d '{
    "branchCode": "28",
    "gafetes": [...]
  }'
```

**Respuesta esperada (400)**:
```json
{
  "success": false,
  "error": "No se pueden sincronizar gafetes de Plaza 28"
}
```

#### Caso 5: Error - Status inválido

```bash
curl -X POST http://localhost:5000/api/gafetes/sync \
  -H "Authorization: Bearer HokaTaxisSync2050" \
  -H "Content-Type: application/json" \
  -d '{
    "branchCode": "CV",
    "gafetes": [{
      "badgeId": "2121",
      "barcode": "2121",
      "status": "X",  # Inválido
      "cycle": 1,
      ...
    }]
  }'
```

**Respuesta esperada (422)**:
```json
{
  "success": false,
  "error": "gafetes[0].status: 'X' no es válido. Permitidos: A, R, S",
  "errorIndex": 0
}
```

---

## Despliegue

### Pre-despliegue (TODO)

- [ ] Revisión técnica
- [ ] Pruebas unitarias (100% cobertura)
- [ ] Pruebas de integración
- [ ] Verificación de seguridad
- [ ] Aprobación del cliente

### Despliegue en Hostinger

1. **Base de datos**
   ```bash
   sqlcmd -S hostname -U user -P password -i 06_CreateCascoGafetesTable.sql
   ```

2. **Backend ASP.NET Core**
   - Copiar archivos a Hostinger
   - Ejecutar migraciones
   - Reiniciar sitio web

3. **Configuración**
   - Agregar connectionString
   - Configurar autenticación Bearer
   - Actualizar appsettings.json

### Activación Cliente Local

1. Descomentar código en `09_CascoGafetesApiClient.cs`
2. Registrar cliente en DI
3. Actualizar `CascoBadgeSyncService` para usar nuevo cliente
4. Recompilar ControlTaxiDesktop.Tools
5. Ejecutar: `casco-badge-sync-one --badge 2121`

### Rollback (si es necesario)

```sql
-- Desactivar sincronización
UPDATE casco_gafetes SET IsActive = 0;

-- O eliminar tabla completa
DROP TABLE casco_gafetes_audit;
DROP TABLE casco_gafetes;
```

---

## Verificaciones Post-despliegue

### Confirmaciones Obligatorias

✓ **Gafete 2121 sincronizado**
```sql
SELECT * FROM casco_gafetes WHERE BadgeId = '2121';
```

✓ **Plaza 28 no tiene gafetes**
```sql
SELECT COUNT(*) FROM casco_gafetes WHERE BranchCode = '28';
-- Esperado: 0
```

✓ **Viajes no modificados**
```sql
SELECT COUNT(*) FROM registros;
-- Debe ser igual al conteo anterior
```

✓ **Tarifas intactas**
```sql
SELECT COUNT(*) FROM tarifas;
-- Debe ser igual al conteo anterior
```

✓ **Auditoría funcionando**
```sql
SELECT COUNT(*) FROM casco_gafetes_audit;
-- Debe tener registros de INSERT
```

---

## Troubleshooting

### Error: "No se puede conectar a base de datos"

```csharp
// Verificar connectionString
var conn = "Server=host;Database=mktCasco;User Id=user;Password=***;";

// Probar conexión
using var sqlConn = new SqlConnection(conn);
sqlConn.Open(); // Lanzará excepción si falla
```

### Error: "Tabla casco_gafetes no existe"

```bash
# Ejecutar migración
sqlcmd -i 06_CreateCascoGafetesTable.sql
```

### Error: "Status X no es válido"

Valores válidos:
- A = Activo
- R = Retirado
- S = Suspendido

### Error: "BadgeId duplicado en payload"

Usar badgeIds únicos o diferentes cycles:
- badge 2121, cycle 1 ← OK
- badge 2121, cycle 2 ← OK
- badge 2121, cycle 1 ← ERROR (duplicado)

### Error: "Gafetes vacío"

Array debe tener al menos 1 gafete:
```json
{
  "branchCode": "CV",
  "gafetes": []  // ERROR
}

{
  "branchCode": "CV",
  "gafetes": [{ ... }]  // OK
}
```

### Lentitud en sincronización

Optimizaciones:
- Usar batch de 100-500 gafetes (no 1000)
- Asegurar índices creados
- Revisar estadísticas SQL: `sp_updatestats`

### Auditoría no se actualiza

Verificar que triggers estén habilitados:
```sql
SELECT * FROM sys.triggers WHERE parent_id = OBJECT_ID('dbo.casco_gafetes');
```

---

## Archivos Entregados

```
Propuestas/Backend/Gafetes/
├── 00_CONTRATO_API.md                      (Este documento)
├── 01_GafetesContracts.cs                  (DTOs/Contratos)
├── 02_GafetesModels.cs                     (Modelos de dominio)
├── 03_GafetesValidator.cs                  (Validador)
├── 04_GafetesSyncService.cs                (Servicio de sincronización)
├── 05_GafetesController.cs                 (Controlador ASP.NET Core)
├── 06_CreateCascoGafetesTable.sql          (Migración SQL)
├── 07_GafetesTests.cs                      (Pruebas unitarias)
├── 08_GafetesServiceCollectionExtensions.cs (Configuración DI)
├── 09_CascoGafetesApiClient.cs             (Cliente local propuesto)
└── 10_DOCUMENTACION_TECNICA.md             (Este archivo)
```

---

## Resumen

| Aspecto | Estado |
|--------|--------|
| **Propuesta completada** | ✓ Sí |
| **Backend implementado** | ✓ Sí (C#) |
| **Base de datos** | ✓ Esquema propuesto |
| **Seguridad** | ✓ Con protecciones |
| **Pruebas** | ✓ Unitarias incluidas |
| **Documentación** | ✓ Completa |
| **Desplegado** | ✗ NO (aún no) |
| **Activado en cliente** | ✗ NO (aún no) |
| **Gafete 2121 sincronizado** | ✗ NO (esperando despliegue) |
| **Plaza 28 protegida** | ✓ SÍ |
| **Viajes intactos** | ✓ SÍ |

---

**Próximo paso**: Revisión y aprobación de propuesta antes de despliegue en Hostinger.
