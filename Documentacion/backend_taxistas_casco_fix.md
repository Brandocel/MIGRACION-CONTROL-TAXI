# Parche Minimo Hostinger - Taxistas Casco

Archivo a editar en Hostinger:

`public_html/casco-api/index.php`

Error confirmado el lunes 20 de julio de 2026:

```json
{
  "error": "Error interno",
  "message": "SQLSTATE[42S22]: Column not found: 1054 Unknown column 'syncStatus' in 'INSERT INTO'"
}
```

La tabla real usada por Casco es:

`mkt2_catalog_taxis_Casco`

Columnas reales esperadas:

- `catalogId`
- `badgeId`
- `driverName`
- `phoneNumber`
- `plate`
- `vehicleModel`
- `unitNumber`
- `serviceType`
- `site`
- `hotel`
- `notes`
- `suggestedAmount`
- `active`
- `createdAt`
- `updatedAt`

## Bloque POST corregido

Busca:

```php
if ($method === 'POST' && $path === '/api/taxis/taxistas') {
```

Deja el `upsert(...)` asi:

```php
upsert($db, 'mkt2_catalog_taxis_Casco', ['catalogId'], [
    'catalogId' => $id,
    'badgeId' => implode(', ', $badgeIds),
    'driverName' => clean($data['driverName'] ?? ''),
    'phoneNumber' => clean($data['phoneNumber'] ?? ''),
    'plate' => clean($data['plate'] ?? ''),
    'vehicleModel' => clean($data['vehicleModel'] ?? ''),
    'unitNumber' => clean($data['unitNumber'] ?? ''),
    'serviceType' => clean($data['serviceType'] ?? ''),
    'site' => clean($data['site'] ?? ''),
    'hotel' => clean($data['hotel'] ?? ''),
    'notes' => clean($data['notes'] ?? ''),
    'suggestedAmount' => isset($data['suggestedAmount']) ? (float)$data['suggestedAmount'] : 0,
    'active' => !array_key_exists('active', $data) || parse_bool($data['active']) ? 1 : 0,
    'createdAt' => date('Y-m-d H:i:s'),
]);
```

## Bloque PUT corregido

Busca:

```php
if (preg_match('#^/api/taxis/taxistas/([0-9]+)$#', $path, $matches) === 1 && $method === 'PUT') {
```

Deja el `upsert(...)` asi:

```php
upsert($db, 'mkt2_catalog_taxis_Casco', ['catalogId'], [
    'catalogId' => $id,
    'badgeId' => implode(', ', $badgeIds),
    'driverName' => clean($data['driverName'] ?? $current['driverName']),
    'phoneNumber' => clean($data['phoneNumber'] ?? $current['phoneNumber']),
    'plate' => clean($data['plate'] ?? $current['plate']),
    'vehicleModel' => clean($data['vehicleModel'] ?? $current['vehicleModel']),
    'unitNumber' => clean($data['unitNumber'] ?? $current['unitNumber']),
    'serviceType' => clean($data['serviceType'] ?? $current['serviceType']),
    'site' => clean($data['site'] ?? $current['site']),
    'hotel' => clean($data['hotel'] ?? $current['hotel']),
    'notes' => clean($data['notes'] ?? $current['notes']),
    'suggestedAmount' => isset($data['suggestedAmount']) ? (float)$data['suggestedAmount'] : (float)$current['suggestedAmount'],
    'active' => array_key_exists('active', $data) ? (parse_bool($data['active']) ? 1 : 0) : ((bool)$current['active'] ? 1 : 0),
    'createdAt' => mysql_datetime($current['createdAt'] ?? null),
    'updatedAt' => date('Y-m-d H:i:s'),
]);
```

## Que quitar

Quitar solo estas lineas del POST y PUT:

```php
'syncStatus' => 'pending',
'source' => 'hostinger',
```

Y quitar tambien estas llamadas para desacoplar Taxistas de Gafetes:

```php
sync_taxista_badges(...)
```

## DELETE correcto

En el bloque:

```php
if (preg_match('#^/api/taxis/taxistas/([0-9]+)$#', $path, $matches) === 1 && $method === 'DELETE') {
```

debe quedar solo:

```php
exec_sql($db, "UPDATE mkt2_catalog_taxis_Casco SET active=0, updatedAt=NOW() WHERE catalogId=?", [$id]);
```

No debe ejecutar ningun `UPDATE mkt2_gafetes_Casco`.

## Importante

No tocar todavia:

- `GET /api/taxis/taxistas`
- rutas de gafetes
- `registros`
- `tarifas`
- `options`
- Plaza 28
- Desktop

## Resultado esperado

- `PUT /casco-api/api/taxis/taxistas/{catalogId}` debe regresar `HTTP 200`
- `POST /casco-api/api/taxis/taxistas` debe dejar de fallar por `syncStatus`
- `DELETE /casco-api/api/taxis/taxistas/{catalogId}` debe desactivar solo el taxista
- `mkt2_gafetes_Casco` no debe cambiar

## Archivo listo para copiar

Tambien deje una copia corregida completa en:

`Documentacion/taxistas_casco_hostinger_index.php`
