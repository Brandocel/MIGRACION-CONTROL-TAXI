# Contrato API: POST /api/gafetes/sync

## Resumen
Ruta nueva y aislada para sincronizar gafetes de Casco.

- **Endpoint**: `POST /casco-api/api/gafetes/sync`
- **Autenticación**: Bearer Token (header `Authorization`)
- **Content-Type**: `application/json`
- **Autorizaciones requeridas**: `sync:gafetes`

---

## Request

### Headers Requeridos
```
Authorization: Bearer {token}
Content-Type: application/json
```

### Body (Contrato PROPUESTO)

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

### Alternativamente (COMPATIBILIDAD)

```json
{
  "branchCode": "CV",
  "mkt2_gafetes": [
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

**Decisión**: Usar el contrato **PROPUESTO** como principal. Aceptar `mkt2_gafetes` solo para retrocompatibilidad con cliente local existente.

---

## Response (Éxito 200 OK)

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

### Campos de Respuesta
- `success` (boolean): Indica si el procesamiento fue exitoso
- `branchCode` (string): Sucursal procesada
- `received` (int): Cantidad total de gafetes recibidos
- `inserted` (int): Gafetes nuevos insertados
- `updated` (int): Gafetes existentes actualizados (status cambió)
- `unchanged` (int): Gafetes que no tuvieron cambios
- `errors` (array): Lista de errores (si aplica)

---

## Validaciones

### 1. BranchCode
- **Requerido**: Sí
- **Válido solo**: `"CV"`
- **Si branchCode != "CV"**: 
  - HTTP 400 Bad Request
  - Mensaje: `"branchCode debe ser 'CV'"`
  - **NUNCA** procesar branchCode `"28"` (Plaza 28 está protegida)

### 2. Gafetes
- **Requerido**: Sí
- **Vacío**: NO permitido
- **Tamaño máximo por lote**: 1000 gafetes
- **Si vacío**:
  - HTTP 400 Bad Request
  - Mensaje: `"gafetes no puede estar vacío"`

### 3. BadgeId (en cada gafete)
- **Requerido**: Sí
- **Tipo**: String
- **Longitud**: 1-50 caracteres
- **Si falta**:
  - HTTP 422 Unprocessable Entity
  - Error por índice: `"gafetes[0].badgeId es obligatorio"`

### 4. Barcode (en cada gafete)
- **Requerido**: Sí
- **Tipo**: String
- **Longitud**: 1-50 caracteres

### 5. Status (en cada gafete)
- **Requerido**: Sí
- **Valores válidos**: `"A"` (Activo), `"R"` (Retirado), `"S"` (Suspendido)
- **Si inválido**:
  - HTTP 422 Unprocessable Entity
  - Error: `"gafetes[0].status: 'X' no es válido. Permitidos: A, R, S"`

### 6. Cycle (en cada gafete)
- **Requerido**: Sí
- **Tipo**: Integer >= 1
- **Si no es entero o < 1**:
  - HTTP 422 Unprocessable Entity
  - Error: `"gafetes[0].cycle debe ser un entero >= 1"`

### 7. CreatedAt (en cada gafete)
- **Requerido**: Sí
- **Formato**: ISO 8601 (`YYYY-MM-DDTHH:MM:SS` o `YYYY-MM-DDTHH:MM:SS.SSSZ`)
- **Si fecha inválida**:
  - HTTP 422 Unprocessable Entity
  - Error: `"gafetes[0].createdAt no es una fecha válida"`

### 8. Duplicados en el mismo payload
- **Validación**: Detectar duplicados por `(branchCode, badgeId, cycle)`
- **Si hay duplicados**:
  - HTTP 422 Unprocessable Entity
  - Error: `"Gafetes duplicados en el payload: badgeId=2121 (cycle=1)"`

### 9. TaxistaId (opcional)
- **Tipo**: Integer o null
- **Rango**: >= 0

### 10. TaxistaName (opcional)
- **Tipo**: String
- **Longitud máxima**: 255 caracteres

---

## Errores HTTP

| Código | Caso | Cuerpo Respuesta |
|--------|------|------------------|
| **400** | branchCode inválido | `{"success": false, "error": "branchCode debe ser 'CV'"}` |
| **400** | gafetes vacío | `{"success": false, "error": "gafetes no puede estar vacío"}` |
| **400** | Tamaño de lote > 1000 | `{"success": false, "error": "Máximo 1000 gafetes por request"}` |
| **401** | Token inválido/falta | `{"success": false, "error": "Autenticación requerida"}` |
| **422** | Status inválido | `{"success": false, "error": "gafetes[0].status: 'X' no es válido. Permitidos: A, R, S", "errorIndex": 0}` |
| **422** | Fecha inválida | `{"success": false, "error": "gafetes[0].createdAt no es una fecha válida", "errorIndex": 0}` |
| **422** | BadgeId faltante | `{"success": false, "error": "gafetes[0].badgeId es obligatorio", "errorIndex": 0}` |
| **422** | Duplicados | `{"success": false, "error": "Gafetes duplicados en el payload: badgeId=2121 (cycle=1)"}` |
| **500** | Error servidor | `{"success": false, "error": "Error interno del servidor"}` |

---

## Reglas de Negocio

### 1. UPSERT por (branch_code, badge_id, cycle)
- **Si no existe**: INSERT
- **Si existe y cambió status**: UPDATE (solo status y updated_at)
- **Si existe igual**: OMITIR (no hacer cambios)

### 2. Idempotencia
- El mismo payload enviado dos veces **NO debe duplicar**
- Prueba:
  - Primer POST: `inserted=1, updated=0, unchanged=0`
  - Segundo POST idéntico: `inserted=0, updated=0, unchanged=1`

### 3. NO Modificar
- ❌ Registros de taxis (tabla taxis)
- ❌ Tarifas (tabla tarifas)
- ❌ Pagos/Comisiones
- ❌ Viajes/Dejadas
- ❌ Plaza 28
- ✅ **Solo gafetes**

### 4. Protección de Plaza 28
```csharp
if (request.BranchCode == "28")
    return BadRequest("No se pueden sincronizar gafetes de Plaza 28");
```

---

## Ejemplos de Prueba

### Caso 1: Payload válido (debe insertar)
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

**Respuesta esperada (200 OK)**:
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

### Caso 2: Duplicado (debe omitir)
Mismo POST anterior enviado nuevamente.

**Respuesta esperada (200 OK)**:
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

### Caso 3: Status inválido
```json
{
  "branchCode": "CV",
  "gafetes": [
    {
      "badgeId": "2121",
      "barcode": "2121",
      "status": "X",
      "cycle": 1,
      "taxistaId": null,
      "taxistaName": "",
      "createdAt": "2026-07-18T11:31:18"
    }
  ]
}
```

**Respuesta esperada (422 Unprocessable Entity)**:
```json
{
  "success": false,
  "error": "gafetes[0].status: 'X' no es válido. Permitidos: A, R, S",
  "errorIndex": 0
}
```

### Caso 4: BranchCode = "28"
```json
{
  "branchCode": "28",
  "gafetes": [...]
}
```

**Respuesta esperada (400 Bad Request)**:
```json
{
  "success": false,
  "error": "No se pueden sincronizar gafetes de Plaza 28"
}
```

### Caso 5: Gafetes vacío
```json
{
  "branchCode": "CV",
  "gafetes": []
}
```

**Respuesta esperada (400 Bad Request)**:
```json
{
  "success": false,
  "error": "gafetes no puede estar vacío"
}
```

### Caso 6: Update (status cambió)
Primera solicitud: status="R"
Segunda solicitud: status="A" (mismo badgeId, cycle)

**Primera respuesta (200 OK)**:
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

**Segunda respuesta (200 OK)**:
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

---

## Rate Limiting (Opcional)

Sugerencia: Implementar rate limiting si existe en el backend:
- Por token: 100 requests/minuto
- Por IP: 1000 requests/minuto
- Tamaño máximo de request: 10 MB

---

## Notas de Seguridad

1. **Autenticación**: Usar el mismo mecanismo que rutas actuales de Casco (Bearer Token)
2. **Content-Type**: Validar que sea `application/json`
3. **CORS**: Permitir solo orígenes de la aplicación local
4. **Logging**: Registrar todos los POST exitosos con timestamp, branchCode, cantidad de gafetes
5. **Auditoría**: Mantener historial de cambios en tabla de auditoría
6. **Protección**: NUNCA permitir branchCode "28"
