# Cambio propuesto de cliente local

Estado: no aplicado.

## URL nueva propuesta

`POST /casco-api/api/gafetes/sync`

## Payload nuevo propuesto

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

## Compatibilidad

Si se desea una transicion sin romper clientes anteriores, el backend puede seguir aceptando:

```json
{
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

## Comportamiento esperado del comando local

Hasta que el backend exista en produccion:

- `casco-badge-sync-one --badge 2121`
- debe mostrar URL nueva
- debe mostrar payload nuevo
- debe mostrar `POST realizado = false`

## Confirmaciones

- No activado.
- No desplegado.
- No modifica Plaza 28.
