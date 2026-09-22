<?php
/*
 * PARCHE ADITIVO - CUADRE PLAZA 28 / CASCO VIEJO PARA HOKA SOLUTIONS
 * Fecha: 2026-09-09
 *
 * QUE HACE
 *   Publica el cuadre completo (las 5 hojas del Excel que genera el escritorio) por HTTP,
 *   para que Hoka Solutions lo consuma igual que ya consume /api/taxis/registros.
 *
 * POR QUE ASI
 *   El cuadre NO se calcula aqui. Las reglas de comision (guias al 8 %, venta duplicada del
 *   mismo taxista, codigos cortos del POS) viven en C# dentro del escritorio y ya costaron
 *   tres sesiones de correcciones. Duplicarlas en PHP garantizaria que Hoka y el Excel den
 *   numeros distintos. Entonces: el escritorio calcula y empuja el resultado ya cuadrado;
 *   este API solo lo guarda y lo devuelve.
 *
 * REGLA DE SEGURIDAD DE ESTE PARCHE
 *   Es 100 % aditivo. No modifica ni una linea existente. Son tres bloques nuevos que se
 *   pegan en index.php, en dos puntos de insercion. Ninguna ruta actual cambia de
 *   comportamiento. Las tablas nuevas usan prefijo mkt2_cuadre_ y no tocan mkt2_trip_records,
 *   mkt2_catalog_taxis, mkt2_pos_operations ni ninguna otra existente.
 *   ensure_cuadre_schema() se invoca UNICAMENTE dentro de las rutas nuevas: si algo fallara,
 *   falla solo el cuadre, no el resto del API.
 *
 * DONDE VA CADA COSA - ver PARCHE-CUADRE-API-2026-09-09-INSTRUCCIONES.md
 */


// ============================================================================
// BLOQUE 1 - pegar al final de index.php, junto a las demas funciones sueltas
// ============================================================================

/**
 * Crea las tablas del cuadre si no existen. Se llama solo desde las rutas del cuadre,
 * nunca desde el arranque del API, para que un error aqui no tumbe el resto.
 *
 * mkt2_cuadre_snapshots guarda el cuadre completo de un rango como un solo JSON. Se eligio
 * una tabla con payload en vez de cinco tablas normalizadas porque el escritorio ya produce
 * la estructura exacta y Hoka la consume tal cual: menos superficie que romper, y cambiar una
 * columna del Excel no obliga a migrar la base.
 */
function ensure_cuadre_schema(PDO $db): void
{
    static $listo = false;
    if ($listo) {
        return;
    }

    exec_sql($db, "CREATE TABLE IF NOT EXISTS mkt2_cuadre_snapshots (
        id INT AUTO_INCREMENT PRIMARY KEY,
        branchCode VARCHAR(40) NOT NULL DEFAULT '',
        fechaInicio DATE NOT NULL,
        fechaFin DATE NOT NULL,
        generatedAt DATETIME NULL,
        receivedAt DATETIME NOT NULL,
        totalRegistros INT NOT NULL DEFAULT 0,
        totalDejada DECIMAL(14,2) NOT NULL DEFAULT 0,
        totalVenta DECIMAL(14,2) NOT NULL DEFAULT 0,
        totalComision DECIMAL(14,2) NOT NULL DEFAULT 0,
        payload LONGTEXT NOT NULL,
        UNIQUE KEY uq_cuadre_rango (branchCode, fechaInicio, fechaFin),
        KEY ix_cuadre_fechas (fechaInicio, fechaFin)
    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci");

    $listo = true;
}

/**
 * Normaliza el codigo de sucursal que pide Hoka. Acepta lo que ya usa el resto del sistema
 * y cae a plaza28 cuando no viene nada, que es el caso de siempre.
 */
function cuadre_branch_code(?string $valor): string
{
    $codigo = strtolower(trim((string)($valor ?? '')));
    $codigo = str_replace([' ', '-', '_'], '', $codigo);

    if ($codigo === '' || $codigo === 'plaza28' || $codigo === 'tiendaplaza28' || $codigo === 'plaza') {
        return 'plaza28';
    }
    if ($codigo === 'casco' || $codigo === 'cascoviejo') {
        return 'cascoviejo';
    }

    return $codigo;
}

/**
 * Estructura vacia del cuadre. Se devuelve cuando no hay snapshot para el rango pedido, para
 * que Hoka pinte la vista sin reventar y muestre "sin datos" en vez de un error 500.
 */
function cuadre_payload_vacio(string $branchCode, string $desde, string $hasta): array
{
    return [
        'branchCode' => $branchCode,
        'fechaInicio' => $desde,
        'fechaFin' => $hasta,
        'generatedAt' => null,
        'disponible' => false,
        'resumen' => [],
        'camiones' => [],
        'dejadas' => [],
        'hoteles' => [],
        'comisiones' => [],
        'corteFinal' => [],
    ];
}


// ============================================================================
// BLOQUE 2 - pegar DENTRO de route_sync(), justo ANTES de su linea
//            json_response(['error' => 'Ruta sync no encontrada', 'path' => $path], 404);
//
// Va aqui porque route_sync() ya exige el X-Sync-Token al entrar (require_sync_token),
// asi que la ruta de escritura queda protegida sin agregar autenticacion nueva.
// ============================================================================

    if ($method === 'POST' && $path === '/sync/push-cuadre') {
        ensure_cuadre_schema($db);
        $data = body();

        $desde = date_param($data['fechaInicio'] ?? null);
        $hasta = date_param($data['fechaFin'] ?? ($data['fechaInicio'] ?? null));
        $sucursal = cuadre_branch_code($data['branchCode'] ?? $branchCode);

        $secciones = ['resumen', 'camiones', 'dejadas', 'hoteles', 'comisiones', 'corteFinal'];
        $payload = [
            'branchCode' => $sucursal,
            'fechaInicio' => $desde,
            'fechaFin' => $hasta,
            'generatedAt' => isset($data['generatedAt']) ? mysql_datetime($data['generatedAt']) : gmdate('c'),
            'disponible' => true,
        ];
        foreach ($secciones as $seccion) {
            $valor = $data[$seccion] ?? [];
            $payload[$seccion] = is_array($valor) ? array_values($valor) : [];
        }

        $json = json_encode($payload, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
        if ($json === false) {
            json_response(['error' => 'No se pudo serializar el cuadre', 'motivo' => json_last_error_msg()], 422);
        }

        upsert($db, 'mkt2_cuadre_snapshots', ['branchCode', 'fechaInicio', 'fechaFin'], [
            'branchCode' => $sucursal,
            'fechaInicio' => $desde,
            'fechaFin' => $hasta,
            'generatedAt' => isset($data['generatedAt']) ? mysql_datetime($data['generatedAt']) : null,
            'receivedAt' => date('Y-m-d H:i:s'),
            'totalRegistros' => count($payload['dejadas']),
            'totalDejada' => (float)($data['totalDejada'] ?? 0),
            'totalVenta' => (float)($data['totalVenta'] ?? 0),
            'totalComision' => (float)($data['totalComision'] ?? 0),
            'payload' => $json,
        ]);

        $conteos = [];
        foreach ($secciones as $seccion) {
            $conteos[$seccion] = count($payload[$seccion]);
        }

        json_response([
            'ok' => true,
            'branchCode' => $sucursal,
            'fechaInicio' => $desde,
            'fechaFin' => $hasta,
            'filas' => $conteos,
            'serverTime' => gmdate('c'),
        ]);
    }


// ============================================================================
// BLOQUE 3 - pegar DENTRO de route_pos_get(), justo ANTES de su linea
//            json_response(['error' => 'Ruta no encontrada', 'path' => $path], 404);
//
// No hace falta tocar el filtro de route() (~linea 718): ese ya manda a route_pos_get
// todo GET que empiece con /api/pos/, y /api/pos/cuadre entra solo.
// ============================================================================

    if ($path === '/api/pos/cuadre') {
        ensure_cuadre_schema($db);

        $sucursal = cuadre_branch_code(query('sucursal') ?? query('branchCode'));
        $desde = date_param(query('dateFrom') ?? query('desde') ?? date('Y-m-d'));
        $hasta = date_param(query('dateTo') ?? query('hasta') ?? $desde);

        $fila = row($db, "SELECT payload, generatedAt, receivedAt
                          FROM mkt2_cuadre_snapshots
                          WHERE branchCode=? AND fechaInicio=? AND fechaFin=?
                          LIMIT 1", [$sucursal, $desde, $hasta]);

        if ($fila === null) {
            json_response(cuadre_payload_vacio($sucursal, $desde, $hasta));
        }

        $payload = json_decode((string)($fila['payload'] ?? ''), true);
        if (!is_array($payload)) {
            json_response(cuadre_payload_vacio($sucursal, $desde, $hasta));
        }

        $payload['receivedAt'] = $fila['receivedAt'] ?? null;
        json_response($payload);
    }

    // Diagnostico: que rangos de cuadre hay guardados. Sirve para saber si el escritorio
    // esta empujando, sin tener que entrar a phpMyAdmin.
    if ($path === '/api/pos/cuadre/disponibles') {
        ensure_cuadre_schema($db);
        json_response(rows($db, "SELECT branchCode, fechaInicio, fechaFin, generatedAt, receivedAt,
                                        totalRegistros, totalDejada, totalVenta, totalComision
                                 FROM mkt2_cuadre_snapshots
                                 ORDER BY fechaInicio DESC, branchCode ASC
                                 LIMIT 200"));
    }
