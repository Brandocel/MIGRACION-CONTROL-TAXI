# Propuesta Backend Casco Gafetes

Estado: propuesta solamente.

No desplegado.
No conectado al flujo activo.
No modifica Plaza 28.
No modifica sincronizador de viajes.
No realiza POST real.

## Ruta propuesta

- Metodo: `POST`
- Ruta completa: `/casco-api/api/gafetes/sync`
- Base viva confirmada: `https://lightyellow-porpoise-679527.hostingersite.com/casco-api/`

## Autenticacion propuesta

Reutilizar el header ya usado por el cliente actual:

- `X-Sync-Token`

## Contrato JSON final propuesto

Se propone aceptar un solo contrato canonico:

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

Compatibilidad opcional de entrada:

- `mkt2_gafetes`

Si llega `mkt2_gafetes`, el backend lo normaliza internamente a `gafetes`.

## Validaciones

- `branchCode` debe ser exactamente `CV`
- `gafetes` obligatorio y no vacio
- `badgeId` obligatorio
- `status` solo `A`, `R`, `S`
- `cycle` entero `>= 1`
- `createdAt` fecha valida ISO-8601
- sin duplicados por `badgeId + cycle` dentro del mismo payload
- tamano maximo de lote sugerido: `200`
- `Content-Type: application/json`

## Persistencia propuesta

No se encontro tabla viva de gafetes en el backend Casco disponible localmente, por lo que la propuesta aislada usa una tabla nueva:

- `casco_gafetes`

Clave unica propuesta:

- `(branch_code, badge_id, cycle)`

## Upsert propuesto

Llave de upsert:

- `branch_code + badge_id + cycle`

Reglas:

- no existe: `INSERT`
- existe y cambia `status`, `barcode`, `taxista_id`, `taxista_name`, `created_at`: `UPDATE`
- existe igual: `UNCHANGED`

## Respuesta propuesta

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

## Idempotencia

El mismo payload dos veces no duplica:

- primer POST: `inserted = 1`
- segundo POST: `unchanged = 1`

## Seguridad

- reutilizar `X-Sync-Token`
- rechazar `branchCode != CV`
- rechazar payloads vacios
- rechazar `status` invalido con `422`
- rechazar `Content-Type` distinto de JSON con `415`

## Archivos de propuesta

- `CascoBadgeSyncController.proposed.cs`
- `CascoBadgeSyncService.proposed.cs`
- `CascoBadgeSyncModels.proposed.cs`
- `casco_gafetes.proposed.sql`
- `CascoBadgeSyncEndpointTests.proposed.cs`
- `CascoBadgeSyncClientChange.proposed.md`

## Confirmaciones

- No se desplego.
- `2121` sigue pendiente.
- Plaza 28 sigue intacta.
