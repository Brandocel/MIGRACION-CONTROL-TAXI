# GUÍA DE DESPLIEGUE: Sincronización de Gafetes de Casco

**IMPORTANTE**: Esta guía es para FUTURO uso. NO EJECUTAR TODAVÍA.

---

## FASE 1: PRE-DESPLIEGUE (Preparación)

### Paso 1.1: Revisión de Seguridad

```csharp
// Verificar en GafetesValidator.cs:
if (request.BranchCode == "28")
    throw new InvalidOperationException("Plaza 28 está protegida");

// Verificar que no accede a otras tablas:
// ❌ registros, tarifas, pagos, viajes, dejadas
// ✓ Solo casco_gafetes, casco_gafetes_audit
```

### Paso 1.2: Revisar Permisos BD

```sql
-- Verificar usuario de sincronización
USE mktCasco;

-- Usuario debe tener:
-- - SELECT, INSERT, UPDATE en casco_gafetes
-- - SELECT, INSERT en casco_gafetes_audit
-- - EXECUTE en sp_UpsertGafete

GRANT SELECT, INSERT, UPDATE ON casco_gafetes TO [sync_user];
GRANT SELECT, INSERT ON casco_gafetes_audit TO [sync_user];
GRANT EXECUTE ON sp_UpsertGafete TO [sync_user];
```

### Paso 1.3: Backup Base de Datos

```sql
-- ANTES de ejecutar migración
BACKUP DATABASE mktCasco
TO DISK = 'D:\Backups\mktCasco_preGafetes.bak'
WITH INIT, COMPRESSION;
```

---

## FASE 2: DESPLIEGUE BASE DE DATOS

### Paso 2.1: Ejecutar Migración SQL

```bash
# En Hostinger (acceso SSH o phpMyAdmin SQL)
sqlcmd -S localhost\SQLEXPRESS \
  -U sa \
  -P "password" \
  -i 06_CreateCascoGafetesTable.sql

# O copiar y ejecutar el contenido en SQL Server Management Studio
```

### Paso 2.2: Verificar Tablas Creadas

```sql
USE mktCasco;

-- Verificar tabla principal
SELECT * FROM sys.tables WHERE name = 'casco_gafetes';

-- Verificar tabla auditoría
SELECT * FROM sys.tables WHERE name = 'casco_gafetes_audit';

-- Verificar índices
SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID('casco_gafetes');

-- Verificar triggers
SELECT * FROM sys.triggers 
WHERE parent_id = OBJECT_ID('casco_gafetes');
```

**Esperado**: 2 tablas, 3 índices, 2 triggers

### Paso 2.3: Verificar Integridad

```sql
-- Verificar que no hay datos en otras tablas después de migración
SELECT 'registros' as Tabla, COUNT(*) as Registros FROM registros
UNION ALL
SELECT 'tarifas', COUNT(*) FROM tarifas
UNION ALL
SELECT 'pagos', COUNT(*) FROM pagos
UNION ALL
SELECT 'viajes', COUNT(*) FROM viajes

-- Todos deben tener el mismo conteo que antes
```

---

## FASE 3: DESPLIEGUE BACKEND

### Paso 3.1: Preparar Archivos

Copiar a servidor:
```
/api/
  /Features/
    /Gafetes/
      /Controllers/
        GafetesController.cs        ← 05_GafetesController.cs
      /Services/
        GafetesValidator.cs         ← 03_GafetesValidator.cs
        GafetesSyncService.cs       ← 04_GafetesSyncService.cs
      /Models/
        Gafete.cs                   ← 02_GafetesModels.cs
      /Contracts/
        SyncGafetesRequest.cs       ← 01_GafetesContracts.cs
      /Infrastructure/
        GafetesServiceCollectionExtensions.cs  ← 08_*
```

### Paso 3.2: Configurar DI en Program.cs

```csharp
// En Program.cs

var builder = WebApplicationBuilder.CreateBuilder(args);

// Agregar servicios de gafetes
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddGafetesServices(connectionString!);

// Agregar autorización
builder.Services.AddCascoApiAuthorization();

// Agregar autenticación (si no existe)
builder.Services
    .AddAuthentication("Bearer")
    .AddScheme<BearerAuthenticationSchemeOptions, BearerAuthenticationHandler>("Bearer", o => { });

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
```

### Paso 3.3: Actualizar appsettings.json

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=REYNA;Database=mktCasco;User Id=sa;Password=280625;TrustServerCertificate=True;Encrypt=False;MultipleActiveResultSets=True;"
  },
  "CascoSync": {
    "ApiBaseUrl": "https://lightyellow-porpoise-679527.hostingersite.com/casco-api/",
    "SyncToken": "HokaTaxisSync2050",
    "BranchCode": "CV"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Features.Gafetes": "Debug"
    }
  }
}
```

### Paso 3.4: Compilar y Desplegar

```bash
# Build
dotnet build --configuration Release

# Publish
dotnet publish --configuration Release --output ./publish

# Copiar a hosting
scp -r ./publish/* user@hostinger:/var/www/casco-api/
```

### Paso 3.5: Reiniciar Servicio

```bash
# En servidor
sudo systemctl restart casco-api

# O si es AppPool en IIS
iisreset /restart
```

---

## FASE 4: VERIFICACIÓN

### Paso 4.1: Probar Endpoint

```bash
# Verificar que endpoint responde
curl -X GET https://lightyellow-porpoise-679527.hostingersite.com/casco-api/api/gafetes/sync \
  -H "Authorization: Bearer HokaTaxisSync2050" \
  -v

# Esperado: 405 Method Not Allowed (GET no permitido, es POST)
```

### Paso 4.2: Probar POST (payload válido)

```bash
curl -X POST https://lightyellow-porpoise-679527.hostingersite.com/casco-api/api/gafetes/sync \
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

# Esperado: 200 OK con {"success": true, "inserted": 1, ...}
```

### Paso 4.3: Verificar Datos Persistidos

```sql
USE mktCasco;

-- 1. Gafete 2121 debe existir
SELECT * FROM casco_gafetes 
WHERE BadgeId = '2121' AND BranchCode = 'CV';

-- 2. Auditoría debe registrar el INSERT
SELECT * FROM casco_gafetes_audit 
ORDER BY AuditId DESC;

-- 3. Viajes no deben cambiar
SELECT COUNT(*) as ViajesCount FROM registros;
-- Comparar con número anterior

-- 4. Plaza 28 debe estar vacía
SELECT COUNT(*) as Plaza28Count FROM casco_gafetes 
WHERE BranchCode = '28';
-- Esperado: 0
```

### Paso 4.4: Probar Idempotencia

```bash
# Enviar mismo payload otra vez
curl -X POST https://lightyellow-porpoise-679527.hostingersite.com/casco-api/api/gafetes/sync \
  -H "Authorization: Bearer HokaTaxisSync2050" \
  -H "Content-Type: application/json" \
  -d '{ ... mismo payload ... }'

# Esperado: 200 OK con {"success": true, "unchanged": 1, "inserted": 0, ...}

# Verificar BD
SELECT * FROM casco_gafetes WHERE BadgeId = '2121';
-- Debe haber 1 solo registro
```

### Paso 4.5: Probar Error (Plaza 28)

```bash
curl -X POST https://lightyellow-porpoise-679527.hostingersite.com/casco-api/api/gafetes/sync \
  -H "Authorization: Bearer HokaTaxisSync2050" \
  -H "Content-Type: application/json" \
  -d '{
    "branchCode": "28",
    "gafetes": [...]
  }'

# Esperado: 400 Bad Request con {"success": false, "error": "..."}
```

### Paso 4.6: Probar Error (Status inválido)

```bash
curl -X POST https://lightyellow-porpoise-679527.hostingersite.com/casco-api/api/gafetes/sync \
  -H "Authorization: Bearer HokaTaxisSync2050" \
  -H "Content-Type: application/json" \
  -d '{
    "branchCode": "CV",
    "gafetes": [{
      ...
      "status": "X"
    }]
  }'

# Esperado: 422 Unprocessable Entity
```

---

## FASE 5: ACTIVACIÓN CLIENTE LOCAL

### Paso 5.1: Descomentar Cliente

En `09_CascoGafetesApiClient.cs`, descomentar:

```csharp
// Pasar de comentado a activo
public sealed class CascoGafetesApiClient : ICascoGafetesApiClient
{
    // ... implementación ...
}
```

### Paso 5.2: Registrar en DI

En `CascoBadgeSyncService` o `Program.cs`:

```csharp
// Registrar cliente
services.AddScoped<ICascoGafetesApiClient>(provider =>
    new CascoGafetesApiClient(
        new HttpClient(),
        configuration["CascoSync:ApiBaseUrl"],
        configuration["CascoSync:BranchCode"]
    )
);
```

### Paso 5.3: Actualizar CascoBadgeSyncService

En `CascoBadgeSyncService.RunWatchCycleAsync()`, reemplazar:

```csharp
// Antes (viejo):
// await httpClient.PostAsync(...)  // No existe

// Después (nuevo):
var gafetesClient = serviceProvider.GetRequiredService<ICascoGafetesApiClient>();
var response = await gafetesClient.SyncMultipleGafetesAsync(payload);

if (response?.Success ?? false)
{
    // Actualizar estado
    updatedEntries[row.BadgeId] = new CascoBadgeSyncEntryState(
        row.BadgeId,
        fingerprint,
        row.Status,
        row.CreatedAt,
        now,
        now,
        true,   // Synced = true
        null,   // Sin errores
        null    // Sin retry
    );
}
```

### Paso 5.4: Recompilar

```bash
cd ControlTaxiDesktop.Tools
dotnet build --configuration Release
```

### Paso 5.5: Probar Comando CLI

```bash
# Ejecutar sincronizador
casco-badge-sync-one --badge 2121

# Esperado en logs:
# INFO: Gafete 2121 sincronizado exitosamente
# Response: {"success": true, "inserted": 1}
```

---

## FASE 6: SINCRONIZACIÓN MASIVA

### Paso 6.1: Ejecutar Auto-sincronización

```bash
# Ejecutar en background (tarea programada)
casco-badge-sync-auto --interval 60 --dry-run false

# Esto debe:
# 1. Leer BD local mktCasco
# 2. Obtener gafetes pendientes
# 3. Enviar POST a /api/gafetes/sync
# 4. Registrar cambios en estado local
# 5. Repetir cada 60 segundos
```

### Paso 6.2: Monitorear Progreso

```sql
-- Ver sincronizaciones completadas
SELECT COUNT(*) as GafetesSincronizados 
FROM casco_gafetes 
WHERE BranchCode = 'CV' AND UpdatedAt > CAST(GETDATE() AS DATE);

-- Ver últimos cambios
SELECT TOP 10 * FROM casco_gafetes_audit 
ORDER BY AuditId DESC;

-- Ver tasa de cambios
SELECT 
    Operation,
    COUNT(*) as Cantidad
FROM casco_gafetes_audit
WHERE ChangedAt > DATEADD(HOUR, -1, GETUTCDATE())
GROUP BY Operation;
```

### Paso 6.3: Validar Completitud

```sql
-- Todos los gafetes deben estar sincronizados
SELECT COUNT(*) as Pendientes 
FROM casco_gafetes 
WHERE SyncedAt IS NULL OR IsActive = 0;
-- Esperado: 0

-- Contador por status
SELECT Status, COUNT(*) as Cantidad 
FROM casco_gafetes 
GROUP BY Status;
```

---

## FASE 7: VERIFICACIÓN FINAL

### Paso 7.1: Checklist

- [ ] Tablas creadas correctamente
- [ ] Índices activos
- [ ] Triggers funcionando
- [ ] Endpoint POST responde
- [ ] Autenticación funciona
- [ ] Gafete 2121 sincronizado
- [ ] Idempotencia confirmada
- [ ] Plaza 28 protegida
- [ ] Viajes intactos
- [ ] Auditoría registrando
- [ ] Auto-sync funcionando
- [ ] Logs sin errores

### Paso 7.2: Documento de Aceptación

```sql
-- Ejecutar para certificar
SELECT 
    (SELECT COUNT(*) FROM casco_gafetes) as TotalGafetes,
    (SELECT COUNT(*) FROM casco_gafetes WHERE BadgeId = '2121') as Gafete2121,
    (SELECT COUNT(*) FROM casco_gafetes WHERE BranchCode = '28') as Plaza28Gafetes,
    (SELECT COUNT(*) FROM registros) as TotalViajes,
    (SELECT COUNT(*) FROM casco_gafetes_audit) as AuditoriaRegistros;

-- Esperado:
-- TotalGafetes: > 0
-- Gafete2121: 1
-- Plaza28Gafetes: 0
-- TotalViajes: (mismo que antes)
-- AuditoriaRegistros: > 0
```

### Paso 7.3: Firma de Aprobación

```
Despliegue completado y verificado: _____ (Fecha)
Responsable: _____
Aprobado por: _____
```

---

## ROLLBACK (si es necesario)

### Escenario 1: Errores Menores

```sql
-- Limpiar gafetes y auditoría
DELETE FROM casco_gafetes_audit;
DELETE FROM casco_gafetes;
-- Las tablas quedan vacías pero funcionales
```

### Escenario 2: Errores Críticos

```sql
-- Restaurar backup
RESTORE DATABASE mktCasco
FROM DISK = 'D:\Backups\mktCasco_preGafetes.bak'
WITH REPLACE;

-- Desplegar versión anterior del backend
-- Reimplementar cliente anterior
```

### Escenario 3: Rollback Completo

```bash
# 1. Desplegar backend anterior
git revert <commit>
dotnet publish --configuration Release
# ... deploy ...

# 2. Dropear tablas (si es necesario)
-- sqlcmd: DROP TABLE casco_gafetes_audit; DROP TABLE casco_gafetes;

# 3. Revertir cliente local
# ... recompilar sin cambios ...

# 4. Restaurar de backup
# ... si fue necesario revertir BD ...
```

---

## MONITOREO POST-DESPLIEGUE

### Alertas Recomendadas

```
1. Si errores > 5 en última hora
   → Investigar logs
   
2. Si no hay sincronizaciones en 2 horas
   → Verificar cliente local
   
3. Si auditoría sin cambios > 1 hora
   → Verificar triggers
   
4. Si respuesta > 5 segundos
   → Revisar índices
```

### Métricas

```sql
-- Ejecutar diariamente
SELECT 
    COUNT(*) as TotalGafetes,
    SUM(CASE WHEN Status = 'A' THEN 1 ELSE 0 END) as Activos,
    SUM(CASE WHEN Status = 'R' THEN 1 ELSE 0 END) as Retirados,
    SUM(CASE WHEN Status = 'S' THEN 1 ELSE 0 END) as Suspendidos,
    DATEDIFF(SECOND, MAX(UpdatedAt), GETUTCDATE()) as SegundosDesdeUltimaActualizacion
FROM casco_gafetes;
```

---

## SOPORTE

Si hay problemas después del despliegue, revisar en orden:

1. **Logs de aplicación**: `/var/log/casco-api/`
2. **Logs SQL Server**: Event Viewer → Applications
3. **Estado de endpoints**: Verificar autenticación Bearer
4. **Base de datos**: `SELECT * FROM casco_gafetes_audit ORDER BY AuditId DESC`
5. **Red**: Verificar conectividad cliente-servidor
6. **Permisos**: Verificar permisos de usuario SQL

---

**IMPORTANTE**: Solo ejecutar esta guía cuando sea autorizado. NO DESPLEGAR TODAVÍA.
