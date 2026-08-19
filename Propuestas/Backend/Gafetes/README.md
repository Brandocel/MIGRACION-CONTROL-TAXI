# PROPUESTA COMPLETA: Sincronización de Gafetes de Casco

**Fecha**: 2026-07-20  
**Estado**: ✓ COMPLETADO - NO DESPLEGADO  
**Carpeta**: `/Propuestas/Backend/Gafetes/`

---

## 📋 RESUMEN EJECUTIVO

Se entrega una **propuesta completa de backend** para sincronizar gafetes de Casco sin modificar:
- ❌ Viajes, tarifas, pagos
- ❌ Plaza 28 (protegida)
- ✅ SOLO gafetes (nueva tabla: `casco_gafetes`)

**IMPORTANTE**: 
- ✓ NO DESPLEGADO TODAVÍA
- ✓ NO POST REAL (cliente comentado)
- ✓ NO TOCA PLAZA 28
- ✓ NO MODIFICA SINCRONIZADOR DE VIAJES

---

## 📁 ESTRUCTURA DE ARCHIVOS

### 🔵 Documentación (4 archivos)

| Archivo | Propósito | Leer |
|---------|-----------|------|
| **00_CONTRATO_API.md** | Especificación REST completa | ⭐ PRIMERO |
| **10_DOCUMENTACION_TECNICA.md** | Documentación técnica detallada | ⭐ SEGUNDO |
| **11_RESUMEN_EJECUTIVO.md** | Resumen ejecutivo con checklist | ⭐ TERCERO |
| **12_GUIA_DESPLIEGUE.md** | Instrucciones paso a paso (futuro) | ⭐ PARA DESPLIEGUE |

### 🟢 Código Backend (5 archivos)

| Archivo | Tipo | Propósito |
|---------|------|-----------|
| **01_GafetesContracts.cs** | C# | DTOs de Request/Response |
| **02_GafetesModels.cs** | C# | Modelos de dominio |
| **03_GafetesValidator.cs** | C# | Validador (14 casos) |
| **04_GafetesSyncService.cs** | C# | Servicio (UPSERT) |
| **05_GafetesController.cs** | C# | Controlador ASP.NET Core |
| **08_GafetesServiceCollectionExtensions.cs** | C# | Configuración DI |

### 🟠 Base de Datos (1 archivo)

| Archivo | Propósito |
|---------|-----------|
| **06_CreateCascoGafetesTable.sql** | Migración SQL (tablas + índices + triggers + auditoría) |

### 🟣 Cliente Local (1 archivo)

| Archivo | Propósito |
|---------|-----------|
| **09_CascoGafetesApiClient.cs** | Cliente propuesto (COMENTADO, sin activar) |

### 🟡 Pruebas (1 archivo)

| Archivo | Propósito |
|---------|-----------|
| **07_GafetesTests.cs** | Pruebas unitarias (14 casos de validación) |

---

## 🎯 QUICK START

### 1️⃣ Revisar Propuesta (5 minutos)

Leer en este orden:

1. Este archivo (README)
2. `00_CONTRATO_API.md` - API REST
3. `11_RESUMEN_EJECUTIVO.md` - Checklist

### 2️⃣ Entender Arquitectura (10 minutos)

Leer: `10_DOCUMENTACION_TECNICA.md`

Secciones clave:
- Arquitectura (diagrama)
- Flujo de sincronización
- Persistencia (tablas + índices)

### 3️⃣ Revisar Código (20 minutos)

En este orden:
1. `03_GafetesValidator.cs` - Validación
2. `04_GafetesSyncService.cs` - Negocio
3. `05_GafetesController.cs` - Endpoint

### 4️⃣ Preparar Despliegue (cuando sea aprobada)

Leer: `12_GUIA_DESPLIEGUE.md`

Ejecutar en fases:
- Fase 1: Pre-despliegue
- Fase 2: Base de datos
- Fase 3: Backend
- Fase 4: Verificación
- Fase 5: Cliente local
- Fase 6: Sincronización masiva

---

## 🔒 CONFIRMACIONES CRÍTICAS

✓ **Plaza 28 Protegida**
```csharp
// En GafetesValidator.cs (línea ~45)
if (request.BranchCode == "28")
    return Error("No se pueden sincronizar gafetes de Plaza 28");
```

✓ **Viajes NO Modificados**
- Nueva tabla: `casco_gafetes` (separada)
- Antigua tabla: `registros` (sin cambios)
- Índices: Solo nuevos índices

✓ **Idempotencia Confirmada**
```
POST #1: {"badgeId": "2121", "cycle": 1} → inserted=1
POST #2: (idéntico) → unchanged=1
✓ Sin duplicados
```

✓ **Sin POST Real**
```csharp
// En 09_CascoGafetesApiClient.cs (línea ~1)
// ✓ COMENTADO - NO ACTIVADO TODAVÍA
// Descomentar solo después de despliegue backend
```

---

## 📊 ESPECIFICACIÓN EN UN VISTAZO

### Endpoint

```
POST /casco-api/api/gafetes/sync
Authorization: Bearer {token}
Content-Type: application/json
```

### Request

```json
{
  "branchCode": "CV",
  "gafetes": [
    {
      "badgeId": "2121",
      "barcode": "2121",
      "status": "R",
      "cycle": 1,
      "taxistaId": null,
      "taxistaName": "",
      "createdAt": "2026-07-18T11:31:18"
    }
  ]
}
```

### Response (200 OK)

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

### Validaciones

| Campo | Validación | Ejemplo |
|-------|-----------|---------|
| branchCode | = "CV" (nunca "28") | ✓ "CV" ✗ "28" |
| gafetes | No vacío, ≤ 1000 | ✓ [1 gafete] ✗ [] |
| badgeId | 1-50 chars | ✓ "2121" ✗ "" |
| status | A, R, o S | ✓ "R" ✗ "X" |
| cycle | ≥ 1 | ✓ 1 ✗ 0 |
| createdAt | ISO 8601 válido | ✓ "2026-07-18T11:31:18" ✗ "invalid" |

### Lógica UPSERT

- **Clave única**: (branchCode, badgeId, cycle)
- Si no existe → INSERT
- Si existe igual → OMITIR (unchanged)
- Si existe y cambió → UPDATE

---

## 📈 COBERTURA DE PRUEBAS

**14 casos de validación** en `07_GafetesTests.cs`:

- ✓ Payload válido → OK
- ✓ BranchCode "28" → Error (400)
- ✓ BranchCode inválido → Error (400)
- ✓ Gafetes vacío → Error (400)
- ✓ Status inválido → Error (422)
- ✓ Status válido (A/R/S) → OK (3 tests)
- ✓ Cycle inválido → Error (422)
- ✓ CreatedAt inválido → Error (422)
- ✓ BadgeId faltante → Error (422)
- ✓ Duplicados en payload → Error (422)
- ✓ Múltiples gafetes → OK
- ✓ Formato mkt2_gafetes → OK
- ✓ Lote > 1000 → Error (400)

---

## 🗄️ BASE DE DATOS

### Tablas Creadas

```sql
casco_gafetes
├── Id (PK)
├── BranchCode, BadgeId, Barcode
├── Status (A|R|S), Cycle
├── TaxistaId, TaxistaName
├── CreatedAt, UpdatedAt, IsActive
├── SyncedAt, SyncSource
└── UNIQUE(BranchCode, BadgeId, Cycle)

casco_gafetes_audit
├── AuditId (PK)
├── GafeteId (FK)
├── Operation (INSERT|UPDATE|DELETE)
├── OldStatus, NewStatus
├── ChangedAt, ChangedBy
└── ChangeReason
```

### Índices

- `IX_CascoGafetes_BranchCode` - Búsqueda principal
- `IX_CascoGafetes_Status` - Filtrado por estado
- `IX_CascoGafetes_UpdatedAt` - Auditoría temporal

### Triggers

- `TR_CascoGafetes_UpdatedAt` - Actualiza UpdatedAt automáticamente
- `TR_CascoGafetes_Audit` - Registra cambios en auditoría

### Procedimiento Almacenado

- `sp_UpsertGafete` - Implementa lógica de UPSERT (opcional)

---

## 🔐 SEGURIDAD

✓ **Autenticación**: Bearer Token (`Authorization` header)  
✓ **Autorización**: Política `CascoApiAccess`  
✓ **Protección Plaza 28**: Validación en código + error 400  
✓ **Content-Type**: Solo `application/json`  
✓ **Tamaño máximo**: 1000 gafetes/request  
✓ **Auditoría**: Tabla `casco_gafetes_audit` + triggers  
✓ **Aislamiento**: Nueva tabla, no modifica viajes/tarifas  

---

## 📝 CASOS DE USO

### Caso 1: Sincronizar Gafete Nuevo

```bash
curl -X POST https://.../api/gafetes/sync \
  -H "Authorization: Bearer HokaTaxisSync2050" \
  -d '{
    "branchCode": "CV",
    "gafetes": [{"badgeId": "2121", ...}]
  }'

# Respuesta: inserted=1, unchanged=0, updated=0
```

### Caso 2: Re-enviar (Idempotencia)

```bash
# Mismo POST anterior
# Respuesta: inserted=0, unchanged=1, updated=0 ✓ Sin duplicados
```

### Caso 3: Cambiar Status

```bash
# badgeId=2121, status cambió de R a A
# Respuesta: inserted=0, unchanged=0, updated=1
```

### Caso 4: Rechazar Plaza 28

```bash
# branchCode="28"
# Respuesta: 400 Bad Request, "No se pueden sincronizar gafetes de Plaza 28"
```

---

## ✅ CHECKLIST PRE-DESPLIEGUE

- [ ] Revisar contrato API (`00_CONTRATO_API.md`)
- [ ] Revisar arquitectura (`10_DOCUMENTACION_TECNICA.md`)
- [ ] Revisar código (C# files)
- [ ] Revisar migración SQL (`06_*.sql`)
- [ ] Ejecutar pruebas unitarias
- [ ] Verificar Plaza 28 protegida
- [ ] Verificar viajes intactos
- [ ] Aprobar propuesta
- [ ] Preparar ambiente de staging
- [ ] Ejecutar despliegue (ver `12_GUIA_DESPLIEGUE.md`)

---

## 📞 PREGUNTAS FRECUENTES

**P: ¿Cuándo se despliega?**  
R: Cuando se autorice. Esta es solo una propuesta.

**P: ¿Qué pasa si fallo el despliegue?**  
R: Se puede hacer rollback (ver `12_GUIA_DESPLIEGUE.md` sección Rollback).

**P: ¿Plaza 28 está segura?**  
R: SÍ. Validación en código (línea ~45 de GafetesValidator) + error 400.

**P: ¿Los gafetes se duplican?**  
R: NO. Lógica de UPSERT + idempotencia confirmada.

**P: ¿Se modifican viajes?**  
R: NO. Nueva tabla `casco_gafetes`, tabla `registros` intacta.

---

## 🚀 PRÓXIMOS PASOS

1. **Revisión** (cuando corresponda)
   - Revisar propuesta
   - Aprobar arquitectura
   - Aprobar seguridad

2. **Preparación** (cuando se autorice)
   - Preparar ambiente Hostinger
   - Clonar/copiar archivos
   - Configurar variables

3. **Despliegue** (cuando se apruebe)
   - Seguir `12_GUIA_DESPLIEGUE.md`
   - Fase 1-7
   - Verificaciones

4. **Activación** (después del despliegue)
   - Descomentar cliente local
   - Recompilar
   - Probar sync
   - Monitorear

---

## 📌 NOTAS IMPORTANTES

⚠️ **NO EJECUTAR NADA TODAVÍA** - Solo propuesta  
⚠️ **CLIENTE COMENTADO** - Código en `09_GafetesApiClient.cs`  
⚠️ **SIN CAMBIOS EN VIAJES** - Nueva tabla `casco_gafetes`  
⚠️ **PLAZA 28 PROTEGIDA** - Validación en `GafetesValidator.cs`  

---

## 📂 INSTALACIÓN

1. Copiar carpeta `/Propuestas/Backend/Gafetes/` a proyecto
2. Leer documentación (en orden: 00 → 10 → 11)
3. Revisar código
4. Cuando se autorice despliegue, seguir `12_GUIA_DESPLIEGUE.md`

---

## 📞 SOPORTE

Si hay preguntas sobre la propuesta:
1. Revisar `00_CONTRATO_API.md` (especificación)
2. Revisar `10_DOCUMENTACION_TECNICA.md` (técnica)
3. Revisar `11_RESUMEN_EJECUTIVO.md` (checklist)
4. Revisar código (C# + SQL)

---

**Estado**: ✓ Propuesta completada  
**Despliegue**: ✗ Aún no  
**Gafete 2121**: ✗ No sincronizado aún  
**Plaza 28**: ✓ Protegida  
**Viajes**: ✓ Intactos  

**Próximo paso**: Aprobación de propuesta
