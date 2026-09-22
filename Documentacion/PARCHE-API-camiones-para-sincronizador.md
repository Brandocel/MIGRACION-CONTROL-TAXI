# Parche del API — camiones fila por fila, para que el sincronizador los baje

Fecha: 4 de septiembre de 2026

## Por qué

El endpoint `/api/pos/camiones` que ya está en el servidor devuelve el **resumen ya sumado**
(empresa, pax, entraron, salieron, unidades, dejada). Sirve para pintar el cuadre, pero no para
sincronizar: el sincronizador necesita los renglones para poder guardarlos en
`dbo.registroscamiones` de SQL Server.

Este parche agrega dos endpoints nuevos:

| Endpoint | Devuelve |
|---|---|
| `GET /api/pos/camiones/registros?desde=&hasta=` | Un renglón por registro de la tabla MySQL `registros`, con la empresa del chofer. Sin fechas, los últimos 7 días. Tope de 20,000 filas. |
| `GET /api/pos/camiones/choferes` | El catálogo completo de `choferes` (800 filas). |

Van dentro de `route_pos_get`, **justo después** del bloque de `/api/pos/camiones` que ya existe
(en la copia local quedó en la línea 1660). Igual que ese, no piden token — son los mismos
endpoints `/api/pos/` que ya consume el escritorio.

## Cómo aplicarlo

**No subir la copia local completa de `index.php`.** El archivo del servidor y la copia local
siguen divergidas (pendiente 4 de la sesión del 28/08). El procedimiento es el mismo del 28/08:

1. Bajar `public_html/index.php` del servidor y respaldarlo como
   `index_bak_2026-09-04.php`.
2. Buscar en ese archivo el bloque que empieza con `if ($path === '/api/pos/camiones') {` y
   termina en `], $filasCamiones));` seguido de su `}`.
3. Pegar **después** de ese `}` el bloque de PHP de abajo.
4. Subir el archivo.
5. Verificar desde cualquier navegador o con curl:

```bash
curl "https://lightyellow-porpoise-679527.hostingersite.com/api/pos/camiones/registros?desde=2026-09-04&hasta=2026-09-04"
```

Debe responder una lista de objetos con `idRegistro`, `idChofer`, `camion`, `pax`, `paxValido`,
`comision`, `fecha`, `hora`, `empresa`. Si responde `{"error":"Ruta no encontrada"}`, el parche no
quedó dentro de `route_pos_get`.

```bash
curl "https://lightyellow-porpoise-679527.hostingersite.com/api/pos/camiones/choferes" | head -c 400
```

6. Con el API respondiendo, el sincronizador de Plaza 28 ya trae el bloque que los baja; se
   verifica en SQL Server:

```sql
SELECT COUNT(*) AS filas, MAX(fecha) AS ultima FROM mkt.dbo.registroscamiones;
```

La fecha máxima debe ser la de hoy. (El 4 de septiembre, la copia de la máquina de desarrollo
estaba en 18/08/2026 y 13,168 filas, contra 14,931 en MySQL de Hostinger.)

## El bloque de PHP

Es idéntico al que quedó aplicado en la copia local
`C:\Users\Administrador\taxis_app\taxis_app\api-php-hostinger\index.php`, líneas 1660 en adelante;
de ahí se puede copiar tal cual.

```php
    // Registros crudos de camiones, para que el sincronizador de Plaza 28 los copie a
    // SQL Server (dbo.registroscamiones). El endpoint de arriba devuelve el resumen ya
    // sumado; este devuelve renglon por renglon, que es lo que necesita el sincronizador
    // para poder actualizar sin recalcular nada.
    if ($path === '/api/pos/camiones/registros') {
        $camiones = camiones_db();
        $desde = clean(query('desde') ?? '');
        $hasta = clean(query('hasta') ?? '');
        if ($desde === '') $desde = date('Y-m-d', strtotime('-6 days'));
        if ($hasta === '') $hasta = date('Y-m-d');

        $filasRegistros = rows($camiones, "
            SELECT r.Id_registros,
                   r.Id_chofer,
                   r.Camion,
                   r.Pax,
                   r.Pax_valido,
                   r.Comision,
                   r.Fecha,
                   r.Hora,
                   UPPER(TRIM(COALESCE(ch.Empresa, ''))) AS empresa
            FROM registros r
            LEFT JOIN choferes ch ON r.Id_chofer = ch.Id_chofer
            WHERE r.Fecha >= ? AND r.Fecha <= ?
            ORDER BY r.Id_registros
            LIMIT 20000", [$desde, $hasta]);

        // PDO regresa los enteros de MySQL como texto; el sincronizador los mete en
        // columnas int/bigint de SQL Server, asi que se convierten aqui.
        json_response(array_map(static fn(array $fila): array => [
            'idRegistro' => (int)$fila['Id_registros'],
            'idChofer' => (string)$fila['Id_chofer'],
            'camion' => (string)$fila['Camion'],
            'pax' => (int)$fila['Pax'],
            'paxValido' => (int)$fila['Pax_valido'],
            'comision' => (int)$fila['Comision'],
            'fecha' => (string)$fila['Fecha'],
            'hora' => (string)$fila['Hora'],
            'empresa' => (string)$fila['empresa'],
        ], $filasRegistros));
    }

    // Catalogo de choferes de camiones. El sincronizador lo copia a dbo.choferes, que es
    // de donde sale la empresa (AUTOCAR / MAYA CARIBE / TURICUN) del cuadre.
    if ($path === '/api/pos/camiones/choferes') {
        $camiones = camiones_db();
        $filasChoferes = rows($camiones, "
            SELECT Id_chofer, Clave, Nombre, Empresa, NumeroTarjeta, Banco, Telefono, activo
            FROM choferes
            ORDER BY Id_chofer");

        json_response(array_map(static fn(array $fila): array => [
            'idChofer' => (string)$fila['Id_chofer'],
            'clave' => (string)($fila['Clave'] ?? ''),
            'nombre' => (string)($fila['Nombre'] ?? ''),
            'empresa' => (string)($fila['Empresa'] ?? ''),
            'numeroTarjeta' => (string)($fila['NumeroTarjeta'] ?? ''),
            'banco' => (string)($fila['Banco'] ?? ''),
            'telefono' => (string)($fila['Telefono'] ?? ''),
            'activo' => (string)($fila['activo'] ?? 'S'),
        ], $filasChoferes));
    }
```

## Qué hace el sincronizador con eso

En `ControlTaxiDesktop\SyncTaxi_Plaza28\sync-sqlserver-hostinger-bidirectional.ps1`, función
`Sync-CamionesFromHostinger`:

- Baja los registros de los últimos **3 días** y los mete en `dbo.registroscamiones` por
  `Id_registros`: si ya está lo actualiza, si no lo inserta.
- **Nunca toca** `pagos`, `fechadepago` ni `Id_tipo` — esas columnas son del POS local y ahí viven
  los pagos capturados en la tienda.
- El catálogo de choferes se baja **cada hora**; los registros, **cada 5 minutos** (la marca de
  tiempo vive en `camiones-ultima-corrida.txt`, junto al script). El loop llama al script cada 15
  segundos y no tiene caso escribir cientos de filas en cada vuelta.
- Crea, si faltan, un índice único en `Id_registros` y otro en `fecha`. La tabla no tenía ninguno,
  y sin ellos cada fila implicaba recorrer 14 mil registros.
- Si el API de camiones falla, se avisa en el log y la sincronización de la app móvil sigue su
  curso; los camiones no pueden tumbar a los taxis.

## Ojo con las dos cuentas de Hostinger

La base de camiones (`u679771392_choferes`, tablas `choferes`, `control`, `registros`,
`tabla_control`, `tabla_control_pax`) está en la cuenta **u679771392**, distinta de la del API y
los taxis (`u265750591_Taxis`). El API llega a ella por host remoto `193.203.166.19` con las
credenciales del bloque `mysql_camiones` de `config.php`. Eso ya está funcionando —
`/api/pos/camiones` responde datos reales — así que este parche no necesita tocar nada de esa
cuenta.
