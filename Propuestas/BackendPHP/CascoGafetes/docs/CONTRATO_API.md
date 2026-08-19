# Contrato API

## Endpoint objetivo

`POST /casco-api/api/gafetes/sync`

## Content-Type

`application/json`

## Headers

- `Content-Type: application/json`
- `X-Sync-Token: <TOKEN>` cuando el entorno lo requiera

## Request principal

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

## Request alterno aceptado

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

## Normalizacion interna

Ambos formatos terminan en una sola lista interna `gafetes`.

## Validaciones

- `branchCode` exactamente `CV`
- rechazar `28`
- lote no vacio
- maximo 500 por request
- `badgeId` obligatorio
- `status` solo `A`, `R` o `S`
- `cycle` entero mayor o igual a 1
- `createdAt` fecha ISO valida
- sin duplicados `badgeId + cycle` dentro del mismo lote

## Respuesta de exito

HTTP 200

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

## Respuesta de validacion

HTTP 422

```json
{
  "success": false,
  "errors": [
    {
      "index": 0,
      "field": "status",
      "message": "Valor invalido"
    }
  ]
}
```

## Respuesta por sucursal incorrecta

HTTP 400

```json
{
  "success": false,
  "error": "branchCode debe ser CV"
}
```

## Respuesta interna

HTTP 500

```json
{
  "success": false,
  "error": "Error interno"
}
```

## Politica transaccional

Este paquete propone transaccion completa por lote.

- Si todo el lote es valido y persistible: commit.
- Si falla un elemento durante persistencia: rollback total.

Se prefiere esta estrategia para no dejar estados mixtos dentro de un mismo envio.
