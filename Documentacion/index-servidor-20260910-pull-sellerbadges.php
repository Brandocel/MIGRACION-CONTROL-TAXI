<?php

declare(strict_types=1);

date_default_timezone_set('America/Mexico_City');

$config = require __DIR__ . '/config.php';

header('Content-Type: application/json; charset=utf-8');
header('Access-Control-Allow-Origin: *');
header('Access-Control-Allow-Headers: Content-Type, Authorization, X-Sync-Token');
header('Access-Control-Allow-Methods: GET, POST, PUT, DELETE, OPTIONS');

if ($_SERVER['REQUEST_METHOD'] === 'OPTIONS') {
    http_response_code(204);
    exit;
}

try {
    route($config);
} catch (Throwable $e) {
    json_response(['error' => 'Error interno', 'message' => $e->getMessage()], 500);
}

function route(array $config): void
{
    $method = $_SERVER['REQUEST_METHOD'];
    $path = parse_url($_SERVER['REQUEST_URI'], PHP_URL_PATH) ?: '/';
    $scriptDir = rtrim(str_replace('\\', '/', dirname($_SERVER['SCRIPT_NAME'] ?? '')), '/');
    if ($scriptDir !== '' && $scriptDir !== '/' && str_starts_with($path, $scriptDir)) {
        $path = substr($path, strlen($scriptDir)) ?: '/';
    }
    $path = '/' . trim($path, '/');
    $db = mysql_db($config);

    if ($path === '/') {
        json_response(['mensaje' => 'API HOKA Taxis MySQL activa OK VENDEDORES 2026-07-29', 'fecha' => gmdate('c')]);
    }
    if ($path === '/health') {
        json_response([
            'ok' => true,
            'mysql' => (int)scalar($db, 'SELECT 1') === 1,
            'fecha' => gmdate('c'),
        ]);
    }

    if (str_starts_with($path, '/sync/')) {
        route_sync($db, $config, $method, $path);
    }

    if (str_starts_with($path, '/api/auth/')) {
        route_auth($db, $config, $method, $path);
    }

    if ($method === 'GET' && $path === '/api/taxis/dashboard') {
        $branchCode = request_branch_code($config);
        $hasRecent = $branchCode === default_branch_code($config)
            && (int)scalar($db, "SELECT COUNT(*) FROM mkt2_dejadas_taxistas_recientes") > 0;
        $hasHotelsCatalog = table_exists($db, 'mkt2_hotels') && (int)scalar($db, "SELECT COUNT(*) FROM mkt2_hotels WHERE active=1") > 0;
        $validHotel = valid_text_sql($hasRecent ? 'origin' : 'hotel');
        $validService = valid_text_sql($hasRecent ? 'transportType' : 'serviceType');
        $catalogBranchWhere = branch_filter_sql($config, 'branchCode', $branchCode);
        json_response([
            'todayRecords' => (int)scalar($db, "SELECT COUNT(*) FROM mkt2_trip_records WHERE recordDate>=CURDATE() AND recordDate<DATE_ADD(CURDATE(), INTERVAL 1 DAY) AND " . branch_filter_sql($config, 'assignedBranchCode', $branchCode), [$branchCode]),
            'activeDrivers' => $hasRecent
                ? (int)scalar($db, "SELECT COUNT(*) FROM mkt2_dejadas_taxistas_recientes WHERE active=1 AND TRIM(driverName)<>''")
                : (int)scalar($db, "SELECT COUNT(DISTINCT NULLIF(TRIM(driverName), '')) FROM mkt2_catalog_taxis WHERE active=1 AND $catalogBranchWhere", [$branchCode]),
            'hotelsCount' => $hasHotelsCatalog
                ? (int)scalar($db, "SELECT COUNT(*) FROM mkt2_hotels WHERE active=1")
                : ($hasRecent
                    ? (int)scalar($db, "SELECT COUNT(DISTINCT TRIM(origin)) FROM mkt2_dejadas_taxistas_recientes WHERE active=1 AND $validHotel")
                    : (int)scalar($db, "SELECT COUNT(DISTINCT TRIM(hotel)) FROM mkt2_catalog_taxis WHERE active=1 AND $validHotel AND $catalogBranchWhere", [$branchCode])),
            'serviceTypesCount' => $hasRecent
                ? (int)scalar($db, "SELECT COUNT(DISTINCT TRIM(transportType)) FROM mkt2_dejadas_taxistas_recientes WHERE active=1 AND $validService")
                : (int)scalar($db, "SELECT COUNT(DISTINCT TRIM(serviceType)) FROM mkt2_catalog_taxis WHERE active=1 AND $validService AND $catalogBranchWhere", [$branchCode]),
        ]);
    }
    if ($method === 'GET' && $path === '/api/taxis/options') {
        $branchCode = request_branch_code($config);
        $hasRecent = $branchCode === default_branch_code($config)
            && (int)scalar($db, "SELECT COUNT(*) FROM mkt2_dejadas_taxistas_recientes") > 0;
        $hasHotelsCatalog = table_exists($db, 'mkt2_hotels') && (int)scalar($db, "SELECT COUNT(*) FROM mkt2_hotels WHERE active=1") > 0;
        $validHotel = valid_text_sql($hasRecent ? 'origin' : 'hotel');
        $validService = valid_text_sql($hasRecent ? 'transportType' : 'serviceType');
        $validSite = valid_text_sql('site');
        $catalogBranchWhere = branch_filter_sql($config, 'branchCode', $branchCode);
        // Tipos de servicio = catalogo de transportes del punto de venta (dbo.transporte,
        // sincronizado a mkt2_pos_transports), NO el DISTINCT de lo ya capturado. Con el
        // historico entraban valores libres como "GUIA", "S/N" o "CALLE": el escritorio
        // despues no los encuentra en el catalogo y termina aplicando la comision por
        // omision. Si la tabla del catalogo todavia no esta sincronizada se mantiene el
        // comportamiento anterior para no dejar la app sin opciones.
        $transportCatalog = table_exists($db, 'mkt2_pos_transports')
            ? column($db, "SELECT DISTINCT TRIM(name) AS item FROM mkt2_pos_transports WHERE TRIM(name)<>'' ORDER BY item")
            : [];
        json_response([
            'hotels' => $hasHotelsCatalog
                ? column($db, "SELECT hotelName AS item FROM mkt2_hotels WHERE active=1 ORDER BY hotelName")
                : ($hasRecent
                    ? column($db, "SELECT DISTINCT TRIM(origin) AS item FROM mkt2_dejadas_taxistas_recientes WHERE active=1 AND $validHotel ORDER BY item")
                    : column($db, "SELECT DISTINCT TRIM(hotel) AS item FROM mkt2_catalog_taxis WHERE active=1 AND $validHotel AND $catalogBranchWhere ORDER BY item", [$branchCode])),
            'serviceTypes' => $transportCatalog !== []
                ? $transportCatalog
                : ($hasRecent
                    ? column($db, "SELECT DISTINCT TRIM(transportType) AS item FROM mkt2_dejadas_taxistas_recientes WHERE active=1 AND $validService ORDER BY item")
                    : column($db, "SELECT DISTINCT TRIM(serviceType) AS item FROM mkt2_catalog_taxis WHERE active=1 AND $validService AND $catalogBranchWhere ORDER BY item", [$branchCode])),
            'sites' => $hasRecent
                ? column($db, "SELECT DISTINCT TRIM(site) AS item FROM mkt2_dejadas_taxistas_recientes WHERE active=1 AND $validSite ORDER BY item")
                : column($db, "SELECT DISTINCT TRIM(site) AS item FROM mkt2_catalog_taxis WHERE active=1 AND $validSite AND $catalogBranchWhere ORDER BY item", [$branchCode]),
        ]);
    }
    if ($method === 'PUT' && $path === '/api/taxis/options') {
        ensure_sync_schema($db);
        $branchCode = request_branch_code($config);
        $data = body();
        $origin = strtoupper(clean($data['origin'] ?? ''));
        $currentValue = clean($data['currentValue'] ?? '');
        $newValue = clean($data['newValue'] ?? '');

        if ($currentValue === '' || $newValue === '') {
            json_response(['error' => 'Valor actual y valor nuevo son requeridos'], 422);
        }

        $catalogColumn = match ($origin) {
            'SERVICIO', 'SERVICE', 'TIPO', 'TIPO_SERVICIO' => 'serviceType',
            'SITIO', 'SITE' => 'site',
            'HOTEL' => 'hotel',
            default => null,
        };

        $recentColumn = match ($origin) {
            'SERVICIO', 'SERVICE', 'TIPO', 'TIPO_SERVICIO' => 'transportType',
            'SITIO', 'SITE' => 'site',
            'HOTEL' => 'origin',
            default => null,
        };

        if ($catalogColumn === null || $recentColumn === null) {
            json_response(['error' => 'Origen no soportado'], 422);
        }

        $updated = 0;
        $catalogWhere = branch_filter_sql($config, 'branchCode', $branchCode);
        $sql = "UPDATE mkt2_catalog_taxis
                SET $catalogColumn=?, updatedAt=NOW(), syncStatus='pending', source='hostinger'
                WHERE active=1
                  AND UPPER(TRIM(COALESCE($catalogColumn, ''))) = UPPER(TRIM(?))
                  AND $catalogWhere";
        $stmt = $db->prepare($sql);
        $stmt->execute([$newValue, $currentValue, $branchCode]);
        $updated += $stmt->rowCount();

        if (table_exists($db, 'mkt2_dejadas_taxistas_recientes')) {
            $sqlRecent = "UPDATE mkt2_dejadas_taxistas_recientes
                          SET $recentColumn=?, updatedAt=NOW()
                          WHERE active=1
                            AND UPPER(TRIM(COALESCE($recentColumn, ''))) = UPPER(TRIM(?))";
            $stmtRecent = $db->prepare($sqlRecent);
            $stmtRecent->execute([$newValue, $currentValue]);
            $updated += $stmtRecent->rowCount();
        }

        json_response([
            'origin' => $origin,
            'previousValue' => $currentValue,
            'newValue' => $newValue,
            'updated' => $updated,
        ]);
    }
    if ($method === 'GET' && $path === '/api/taxis/catalogo') {
        $branchCode = request_branch_code($config);
        $q = clean(query('query'));
        if ($q !== '') {
            ensure_sync_schema($db);
            $badgeItem = find_catalog_by_linked_gafete($db, $q, $branchCode);
            if ($badgeItem) {
                json_response($badgeItem);
            }
        }
        $hasRecent = $branchCode === default_branch_code($config)
            && (int)scalar($db, "SELECT COUNT(*) FROM mkt2_dejadas_taxistas_recientes") > 0;
        if ($q !== '' && $hasRecent) {
            $like = "%$q%";
            $item = row($db, "SELECT catalogId,badgeId,driverName,phoneNumber,plate,vehicleModel,unitNumber,serviceType,site,hotel,notes,suggestedAmount
                FROM (
                SELECT
                    900000000 + recentId AS catalogId,
                    lastFolio AS badgeId,
                    driverName,
                    phoneNumber,
                    CASE WHEN unitNumber REGEXP '[[:alpha:]]' AND unitNumber LIKE '%-%' THEN unitNumber ELSE '' END AS plate,
                    '' AS vehicleModel,
                    unitNumber,
                    transportType AS serviceType,
                    site,
                    origin AS hotel,
                    '' AS notes,
                    suggestedAmount,
                    COALESCE(lastDate, DATE('1000-01-01')) AS sortDate,
                    CAST(NULLIF(lastFolio, '') AS UNSIGNED) AS sortFolio,
                    0 AS sourceRank
                FROM mkt2_dejadas_taxistas_recientes
                WHERE active=1 AND (lastFolio=? OR driverName LIKE ? OR unitNumber LIKE ? OR phoneNumber LIKE ?)

                UNION ALL

                SELECT
                    catalogId,
                    badgeId,
                    driverName,
                    phoneNumber,
                    plate,
                    vehicleModel,
                    unitNumber,
                    serviceType,
                    site,
                    hotel,
                    CASE
                        WHEN LOWER(notes) LIKE '%importado desde dejadas%' OR LOWER(notes) LIKE '%origen%' OR LOWER(notes) LIKE '%mkt%' THEN ''
                        ELSE notes
                    END AS notes,
                    suggestedAmount,
                    DATE(COALESCE(updatedAt, createdAt, NOW())) AS sortDate,
                    CAST(NULLIF(badgeId, '') AS UNSIGNED) AS sortFolio,
                    1 AS sourceRank
                FROM mkt2_catalog_taxis
                WHERE active=1 AND catalogId<900000000 AND " . branch_filter_sql($config, 'branchCode', $branchCode) . " AND (badgeId=? OR driverName LIKE ? OR plate LIKE ? OR unitNumber LIKE ?)
                ) t
                ORDER BY CASE WHEN badgeId=? THEN 0 ELSE 1 END, sourceRank ASC, sortDate DESC, sortFolio DESC, driverName
                LIMIT 1", [$q, $like, $like, $like, $branchCode, $q, $like, $like, $like, $q]);
        } else {
            $item = $q === '' ? null : row($db, "SELECT catalogId,badgeId,driverName,phoneNumber,plate,vehicleModel,unitNumber,serviceType,site,hotel,CASE WHEN LOWER(notes) LIKE '%importado desde dejadas%' OR LOWER(notes) LIKE '%origen%' OR LOWER(notes) LIKE '%mkt%' THEN '' ELSE notes END AS notes,suggestedAmount FROM mkt2_catalog_taxis WHERE active=1 AND " . branch_filter_sql($config, 'branchCode', $branchCode) . " AND (badgeId=? OR driverName LIKE ? OR plate LIKE ? OR unitNumber LIKE ?) ORDER BY CASE WHEN badgeId=? THEN 0 ELSE 1 END, driverName LIMIT 1", [$branchCode, $q, "%$q%", "%$q%", "%$q%", $q]);
        }
        $item ? json_response($item) : json_response(null, 404);
    }
    if ($method === 'GET' && $path === '/api/taxis/catalogo/buscar') {
        $branchCode = request_branch_code($config);
        $q = clean(query('query'));
        if ($q !== '') {
            ensure_sync_schema($db);
            $badgeItem = find_catalog_by_linked_gafete($db, $q, $branchCode);
            if ($badgeItem) {
                json_response([$badgeItem]);
            }
        }
        $hasRecent = $branchCode === default_branch_code($config)
            && (int)scalar($db, "SELECT COUNT(*) FROM mkt2_dejadas_taxistas_recientes") > 0;
        if ($q === '') {
            json_response([]);
        }
        if ($hasRecent) {
            $like = "%$q%";
            json_response(rows($db, "SELECT catalogId,badgeId,driverName,phoneNumber,plate,vehicleModel,unitNumber,serviceType,site,hotel,notes,suggestedAmount
                FROM (
                SELECT
                    900000000 + recentId AS catalogId,
                    lastFolio AS badgeId,
                    driverName,
                    phoneNumber,
                    CASE WHEN unitNumber REGEXP '[[:alpha:]]' AND unitNumber LIKE '%-%' THEN unitNumber ELSE '' END AS plate,
                    '' AS vehicleModel,
                    unitNumber,
                    transportType AS serviceType,
                    site,
                    origin AS hotel,
                    '' AS notes,
                    suggestedAmount,
                    COALESCE(lastDate, DATE('1000-01-01')) AS sortDate,
                    CAST(NULLIF(lastFolio, '') AS UNSIGNED) AS sortFolio,
                    0 AS sourceRank
                FROM mkt2_dejadas_taxistas_recientes
                WHERE active=1 AND (driverName LIKE ? OR lastFolio LIKE ? OR unitNumber LIKE ? OR phoneNumber LIKE ?)

                UNION ALL

                SELECT
                    catalogId,
                    badgeId,
                    driverName,
                    phoneNumber,
                    plate,
                    vehicleModel,
                    unitNumber,
                    serviceType,
                    site,
                    hotel,
                    CASE
                        WHEN LOWER(notes) LIKE '%importado desde dejadas%' OR LOWER(notes) LIKE '%origen%' OR LOWER(notes) LIKE '%mkt%' THEN ''
                        ELSE notes
                    END AS notes,
                    suggestedAmount,
                    DATE(COALESCE(updatedAt, createdAt, NOW())) AS sortDate,
                    CAST(NULLIF(badgeId, '') AS UNSIGNED) AS sortFolio,
                    1 AS sourceRank
                FROM mkt2_catalog_taxis
                WHERE active=1 AND catalogId<900000000 AND " . branch_filter_sql($config, 'branchCode', $branchCode) . " AND (driverName LIKE ? OR badgeId LIKE ? OR plate LIKE ? OR unitNumber LIKE ?)
                ) t
                ORDER BY sourceRank ASC, sortDate DESC, sortFolio DESC, driverName
                LIMIT 8", [$like, $like, $like, $like, $branchCode, $like, $like, $like, $like]));
        }
        json_response(rows($db, "SELECT catalogId,badgeId,driverName,phoneNumber,plate,vehicleModel,unitNumber,serviceType,site,hotel,CASE WHEN LOWER(notes) LIKE '%importado desde dejadas%' OR LOWER(notes) LIKE '%origen%' OR LOWER(notes) LIKE '%mkt%' THEN '' ELSE notes END AS notes,suggestedAmount FROM mkt2_catalog_taxis WHERE active=1 AND " . branch_filter_sql($config, 'branchCode', $branchCode) . " AND (driverName LIKE ? OR badgeId LIKE ? OR plate LIKE ? OR unitNumber LIKE ?) ORDER BY driverName LIMIT 8", [$branchCode, "%$q%", "%$q%", "%$q%", "%$q%"]));
    }
    if ($method === 'GET' && $path === '/api/taxis/tarifas') {
        $branchCode = request_branch_code($config);
        if (table_exists($db, 'mkt2_service_rate_rules') && (int)scalar($db, "SELECT COUNT(*) FROM mkt2_service_rate_rules WHERE active=1 AND " . branch_filter_sql($config, 'branchCode', $branchCode), [$branchCode]) > 0) {
            json_response(rows($db, "SELECT serviceType,description,amount AS baseAmount, CAST(0 AS DECIMAL(18,2)) AS extraPersonAmount, origin, minPassengers, maxPassengers FROM mkt2_service_rate_rules WHERE active=1 AND " . branch_filter_sql($config, 'branchCode', $branchCode) . " ORDER BY serviceType, origin, minPassengers", [$branchCode]));
        }
        $validService = valid_text_sql('serviceType');
        json_response(rows($db, "SELECT serviceType,description,baseAmount,extraPersonAmount,'' AS origin,NULL AS minPassengers,NULL AS maxPassengers FROM mkt2_service_rates WHERE $validService AND " . branch_filter_sql($config, 'branchCode', $branchCode) . " ORDER BY serviceType", [$branchCode]));
    }
    if ($method === 'GET' && $path === '/api/taxis/taxistas') {
        $branchCode = request_branch_code($config);
        $page = max(1, (int)(query('page') ?? 1));
        $pageSize = min(100, max(1, (int)(query('pageSize') ?? 100)));
        $offset = ($page - 1) * $pageSize;
        $q = clean(query('query'));
        $active = clean(query('active'));
        $hasBranchCatalog = (int)scalar(
            $db,
            "SELECT COUNT(*) FROM mkt2_catalog_taxis WHERE active=1 AND " . branch_filter_sql($config, 'branchCode', $branchCode),
            [$branchCode],
        ) > 0;

        if (
            !$hasBranchCatalog
            && $branchCode === default_branch_code($config)
            && (int)scalar($db, "SELECT COUNT(*) FROM mkt2_dejadas_taxistas_recientes") > 0
        ) {
            $recentWhere = "TRIM(driverName)<>''";
            $catalogWhere = "catalogId<900000000";
            $recentParams = [];
            $catalogParams = [];
            if ($q !== '') {
                $recentWhere .= " AND (CAST(recentId AS CHAR) LIKE ? OR driverName LIKE ? OR phoneNumber LIKE ? OR transportType LIKE ? OR unitNumber LIKE ? OR site LIKE ? OR origin LIKE ? OR lastFolio LIKE ?)";
                $catalogWhere .= " AND (CAST(catalogId AS CHAR) LIKE ? OR driverName LIKE ? OR phoneNumber LIKE ? OR serviceType LIKE ? OR plate LIKE ? OR unitNumber LIKE ? OR site LIKE ? OR hotel LIKE ?)";
                $like = "%$q%";
                array_push($recentParams, $like, $like, $like, $like, $like, $like, $like, $like);
                array_push($catalogParams, $like, $like, $like, $like, $like, $like, $like, $like);
            }
            if ($active === '1' || $active === '0') {
                $recentWhere .= " AND active=?";
                $catalogWhere .= " AND active=?";
                $recentParams[] = (int)$active;
                $catalogParams[] = (int)$active;
            }
            if ($q === '') {
                $total = (int)scalar($db, "SELECT COUNT(*) FROM mkt2_dejadas_taxistas_recientes WHERE $recentWhere", $recentParams);
                $items = rows($db, "SELECT
                        900000000 + recentId AS catalogId,
                        lastFolio AS badgeId,
                        driverName,
                        phoneNumber,
                        CASE WHEN unitNumber REGEXP '[[:alpha:]]' AND unitNumber LIKE '%-%' THEN unitNumber ELSE '' END AS plate,
                        '' AS vehicleModel,
                        unitNumber,
                        transportType AS serviceType,
                        site,
                        origin AS hotel,
                        '' AS notes,
                        active
                    FROM mkt2_dejadas_taxistas_recientes
                    WHERE $recentWhere
                    ORDER BY active DESC, COALESCE(lastDate, DATE('1000-01-01')) DESC, CAST(NULLIF(lastFolio, '') AS UNSIGNED) DESC, driverName
                    LIMIT $pageSize OFFSET $offset", $recentParams);
                json_response(['items' => $items, 'page' => $page, 'pageSize' => $pageSize, 'total' => $total]);
            }
            $total =
                (int)scalar($db, "SELECT COUNT(*) FROM mkt2_dejadas_taxistas_recientes WHERE $recentWhere", $recentParams)
                + (int)scalar($db, "SELECT COUNT(*) FROM mkt2_catalog_taxis WHERE $catalogWhere", $catalogParams);
            $items = rows($db, "SELECT catalogId,badgeId,driverName,phoneNumber,plate,vehicleModel,unitNumber,serviceType,site,hotel,notes,active
                FROM (
                    SELECT
                        900000000 + recentId AS catalogId,
                        lastFolio AS badgeId,
                        driverName,
                        phoneNumber,
                        CASE WHEN unitNumber REGEXP '[[:alpha:]]' AND unitNumber LIKE '%-%' THEN unitNumber ELSE '' END AS plate,
                        '' AS vehicleModel,
                        unitNumber,
                        transportType AS serviceType,
                        site,
                        origin AS hotel,
                        '' AS notes,
                        active,
                        COALESCE(lastDate, DATE('1000-01-01')) AS sortDate,
                        CAST(NULLIF(lastFolio, '') AS UNSIGNED) AS sortFolio,
                        0 AS sourceRank
                    FROM mkt2_dejadas_taxistas_recientes
                    WHERE $recentWhere

                    UNION ALL

                    SELECT
                        catalogId,
                        badgeId,
                        driverName,
                        phoneNumber,
                        plate,
                        vehicleModel,
                        unitNumber,
                        serviceType,
                        site,
                        hotel,
                        CASE
                            WHEN LOWER(notes) LIKE '%importado desde dejadas%' OR LOWER(notes) LIKE '%origen%' OR LOWER(notes) LIKE '%mkt%' THEN ''
                            ELSE notes
                        END AS notes,
                        active,
                        DATE(COALESCE(updatedAt, createdAt, NOW())) AS sortDate,
                        CAST(NULLIF(badgeId, '') AS UNSIGNED) AS sortFolio,
                        -1 AS sourceRank
                    FROM mkt2_catalog_taxis
                    WHERE $catalogWhere
                ) t
                ORDER BY active DESC, sourceRank ASC, sortDate DESC, sortFolio DESC, driverName
                LIMIT $pageSize OFFSET $offset", array_merge($recentParams, $catalogParams));
            json_response(['items' => $items, 'page' => $page, 'pageSize' => $pageSize, 'total' => $total]);
        }

        $where = branch_filter_sql($config, 'branchCode', $branchCode);
        $params = [$branchCode];
        if ($q !== '') {
            $where .= " AND (CAST(catalogId AS CHAR) LIKE ? OR driverName LIKE ? OR phoneNumber LIKE ? OR serviceType LIKE ? OR plate LIKE ? OR unitNumber LIKE ? OR site LIKE ? OR hotel LIKE ?)";
            array_push($params, "%$q%", "%$q%", "%$q%", "%$q%", "%$q%", "%$q%", "%$q%", "%$q%");
        }
        if ($active === '1' || $active === '0') {
            $where .= " AND active=?";
            $params[] = (int)$active;
        }
        $total = (int)scalar($db, "SELECT COUNT(*) FROM mkt2_catalog_taxis WHERE $where", $params);
        $items = rows($db, "SELECT catalogId,badgeId,driverName,phoneNumber,plate,vehicleModel,unitNumber,serviceType,site,hotel,CASE WHEN LOWER(notes) LIKE '%importado desde dejadas%' OR LOWER(notes) LIKE '%origen%' OR LOWER(notes) LIKE '%mkt%' THEN '' ELSE notes END AS notes,active FROM mkt2_catalog_taxis WHERE $where ORDER BY active DESC, driverName LIMIT $pageSize OFFSET $offset", $params);
        json_response(['items' => $items, 'page' => $page, 'pageSize' => $pageSize, 'total' => $total]);
    }
    if (($method === 'POST' && $path === '/api/taxis/taxistas') || ($method === 'PUT' && preg_match('#^/api/taxis/taxistas/(\d+)$#', $path, $m))) {
        ensure_catalog_schema($db);
        ensure_sync_schema($db);
        ensure_dejadas_schema($db);
        $data = body();
        $id = isset($m[1]) ? (int)$m[1] : (int)($data['catalogId'] ?? 0);
        $branchCode = request_branch_code($config);
        $badgeIds = normalize_badges_input($data['badgeIds'] ?? null, $data['badgeId'] ?? '');
        $badgeSummary = implode(', ', $badgeIds);
        if ($method === 'PUT' && $id >= 900000000) {
            $recentId = $id - 900000000;
            $driverName = clean($data['driverName'] ?? '');
            $unitNumber = clean($data['unitNumber'] ?? '');
            $phoneNumber = clean($data['phoneNumber'] ?? '');
            exec_sql($db, "UPDATE mkt2_dejadas_taxistas_recientes
                SET driverName=?,
                    driverKey=?,
                    unitNumber=?,
                    phoneNumber=?,
                    transportType=?,
                    site=?,
                    origin=?,
                    lastFolio=?,
                    active=?
                WHERE recentId=?", [
                $driverName,
                $driverName . '|' . $unitNumber . '|' . $phoneNumber,
                $unitNumber,
                $phoneNumber,
                clean($data['serviceType'] ?? ''),
                clean($data['site'] ?? ''),
                clean($data['hotel'] ?? ''),
                $badgeSummary,
                !array_key_exists('active', $data) || (bool)$data['active'] ? 1 : 0,
                $recentId,
            ]);
            sync_taxista_badges($db, $id, clean($data['driverName'] ?? ''), $badgeIds, !array_key_exists('active', $data) || (bool)$data['active'], $branchCode);
            json_response(row($db, "SELECT
                    900000000 + recentId AS catalogId,
                    lastFolio AS badgeId,
                    driverName,
                    phoneNumber,
                    CASE WHEN unitNumber REGEXP '[[:alpha:]]' AND unitNumber LIKE '%-%' THEN unitNumber ELSE '' END AS plate,
                    '' AS vehicleModel,
                    unitNumber,
                    transportType AS serviceType,
                    site,
                    origin AS hotel,
                    '' AS notes,
                    active
                FROM mkt2_dejadas_taxistas_recientes
                WHERE recentId=?", [$recentId]));
        }
        if ($id <= 0) {
            $id = (int)scalar($db, "SELECT COALESCE(MAX(CASE WHEN catalogId<900000000 THEN catalogId ELSE 0 END),0)+1 FROM mkt2_catalog_taxis");
        }
        upsert($db, 'mkt2_catalog_taxis', ['catalogId'], [
            'catalogId' => $id,
            'badgeId' => $badgeSummary,
            'driverName' => clean($data['driverName'] ?? ''),
            'phoneNumber' => clean($data['phoneNumber'] ?? ''),
            'plate' => clean($data['plate'] ?? ''),
            'vehicleModel' => clean($data['vehicleModel'] ?? ''),
            'unitNumber' => clean($data['unitNumber'] ?? ''),
            'serviceType' => clean($data['serviceType'] ?? ''),
            'site' => clean($data['site'] ?? ''),
            'hotel' => clean($data['hotel'] ?? ''),
            'notes' => clean($data['notes'] ?? ''),
            'suggestedAmount' => 0,
            'active' => !array_key_exists('active', $data) || (bool)$data['active'] ? 1 : 0,
            'syncStatus' => 'pending',
            'source' => 'hostinger',
            'branchCode' => $branchCode,
        ]);
        sync_taxista_badges($db, $id, clean($data['driverName'] ?? ''), $badgeIds, !array_key_exists('active', $data) || (bool)$data['active'], $branchCode);
        json_response(row($db, "SELECT catalogId,badgeId,driverName,phoneNumber,plate,vehicleModel,unitNumber,serviceType,site,hotel,CASE WHEN LOWER(notes) LIKE '%importado desde dejadas%' OR LOWER(notes) LIKE '%origen%' OR LOWER(notes) LIKE '%mkt%' THEN '' ELSE notes END AS notes,suggestedAmount,active FROM mkt2_catalog_taxis WHERE catalogId=?", [$id]));
    }
    if ($method === 'DELETE' && preg_match('#^/api/taxis/taxistas/(\d+)$#', $path, $m)) {
        ensure_catalog_schema($db);
        ensure_sync_schema($db);
        ensure_dejadas_schema($db);
        $id = (int)$m[1];
        $branchCode = request_branch_code($config);
        if ($id >= 900000000) {
            exec_sql($db, "UPDATE mkt2_dejadas_taxistas_recientes SET active=0 WHERE recentId=?", [$id - 900000000]);
        } else {
            exec_sql($db, "UPDATE mkt2_catalog_taxis SET active=0, syncStatus='pending', source='hostinger' WHERE catalogId=? AND " . branch_filter_sql($config, 'branchCode', $branchCode), [$id, $branchCode]);
        }
        json_response(['deleted' => true]);
    }
    if ($method === 'GET' && ($path === '/api/taxis/registros' || $path === '/api/taxis/history')) {
        ensure_app_schema($db);
        $from = date_param(query('dateFrom') ?: query('date') ?: date('Y-m-d'));
        $to = date_param(query('dateTo') ?: query('date') ?: $from);
        $q = clean(query('query'));
        $branch = clean(query('assignedBranch'));
        $branchCode = clean(query('assignedBranchCode') ?? query('branchCode') ?? '');
        $params = [$from, $to];
        $where = "recordDate>=? AND recordDate<DATE_ADD(?, INTERVAL 1 DAY)";
        if ($q !== '') {
            $where .= " AND (recordId LIKE ? OR badgeId LIKE ? OR driverName LIKE ? OR hotel LIKE ? OR assignedBranch LIKE ?)";
            array_push($params, "%$q%", "%$q%", "%$q%", "%$q%", "%$q%");
        }
        if ($branchCode !== '') {
            $where .= " AND assignedBranchCode=?";
            $params[] = $branchCode;
        }
        if ($branch !== '') {
            $where .= " AND assignedBranch=?";
            $params[] = $branch;
        }
        json_response(attach_trip_record_sellers($db, rows($db, "SELECT recordId,catalogId,badgeId,driverName,driverPhone,contactPhone,nationality,plate,vehicleModel,unitNumber,hotel,origin,site,destination,sellerKey,sellerName,adultCount,youthCount,minorCount,noShowCount,passengerCount,serviceType,tripCost,paymentMethod,notes,recordDate,assignedBranch,assignedBranchCode,assignedBranchName,payoutStatus,payoutDate,payoutUser,payoutTicket FROM mkt2_trip_records WHERE $where ORDER BY recordDate DESC LIMIT 500", $params)));
    }
    if ($method === 'POST' && $path === '/api/taxis/registros') {
        ensure_app_schema($db);
        $data = body();
        $recordId = next_trip_record_id($db);
        $branchCode = clean($data['assignedBranchCode'] ?? '');
        $branchName = clean($data['assignedBranchName'] ?? '');
        $assignedBranch = clean($data['assignedBranch'] ?? trim($branchCode . ' - ' . $branchName, ' -'));
        $badgeIds = normalize_badges_input($data['badgeIds'] ?? null, $data['badgeId'] ?? '');
        $badgeSummary = implode(', ', $badgeIds);
        $sellerPairs = normalize_seller_badges($data['sellerBadges'] ?? null, $badgeIds, clean($data['sellerKey'] ?? ''), clean($data['sellerName'] ?? ''));
        $primarySeller = primary_seller_from_badges($sellerPairs);
        save_trip_record($db, [
            'recordId' => $recordId,
            'catalogId' => $data['catalogId'] ?? null,
            'badgeId' => $badgeSummary,
            'driverName' => clean($data['driverName'] ?? ''),
            'driverPhone' => clean($data['driverPhone'] ?? ''),
            'contactPhone' => clean($data['contactPhone'] ?? ''),
            'nationality' => clean($data['nationality'] ?? ''),
            'plate' => clean($data['plate'] ?? ''),
            'vehicleModel' => clean($data['vehicleModel'] ?? ''),
            'unitNumber' => clean($data['unitNumber'] ?? ''),
            'hotel' => clean($data['hotel'] ?? ''),
            'origin' => clean($data['origin'] ?? ''),
            'site' => clean($data['site'] ?? ''),
            'destination' => clean($data['destination'] ?? ''),
            'sellerKey' => $primarySeller['sellerKey'],
            'sellerName' => $primarySeller['sellerName'],
            'adultCount' => non_negative_int($data['adultCount'] ?? 0),
            'youthCount' => non_negative_int($data['youthCount'] ?? 0),
            'minorCount' => non_negative_int($data['minorCount'] ?? 0),
            'noShowCount' => non_negative_int($data['noShowCount'] ?? 0),
            'passengerCount' => (int)($data['passengerCount'] ?? 0),
            'serviceType' => clean($data['serviceType'] ?? ''),
            'tripCost' => (float)($data['tripCost'] ?? 0),
            'paymentMethod' => clean($data['paymentMethod'] ?? ''),
            'notes' => clean($data['notes'] ?? ''),
            'recordDate' => mysql_datetime($data['recordDate'] ?? null),
            'assignedBranch' => $assignedBranch,
            'assignedBranchCode' => $branchCode,
            'assignedBranchName' => $branchName,
            'payoutStatus' => 'pendiente',
            'payoutDate' => null,
            'payoutUser' => '',
            'payoutTicket' => '',
            'syncStatus' => 'pending',
            'source' => 'hostinger',
        ]);
        save_trip_record_sellers($db, $recordId, $sellerPairs, $branchCode);
        sync_taxista_badges_by_seller($db, isset($data['catalogId']) ? (int)$data['catalogId'] : null, clean($data['driverName'] ?? ''), $sellerPairs, true, $branchCode);
        json_response(['recordId' => $recordId, 'recordDate' => date('c'), 'sellerBadges' => $sellerPairs]);
    }
    if ($method === 'PUT' && preg_match('#^/api/taxis/registros/([^/]+)$#', $path, $m)) {
        ensure_app_schema($db);
        $recordId = clean(urldecode($m[1]));
        $data = body();
        $rawBody = request_raw_body();
        if ($data === []) {
            json_response([
                'error' => 'El PUT no recibió datos válidos',
                'contentType' => $_SERVER['CONTENT_TYPE'] ?? '',
                'rawBody' => $rawBody,
                'receivedKeys' => [],
                'received' => [
                    'tripCost' => '**NO_RECIBIDO**',
                    'driverName' => '**NO_RECIBIDO**',
                    'catalogId' => '**NO_RECIBIDO**',
                ],
            ], 422);
        }
        $current = row($db, "SELECT recordId,catalogId,badgeId,driverName,driverPhone,contactPhone,nationality,plate,vehicleModel,unitNumber,hotel,origin,site,destination,sellerKey,sellerName,adultCount,youthCount,minorCount,noShowCount,passengerCount,serviceType,tripCost,paymentMethod,notes,recordDate,assignedBranch,assignedBranchCode,assignedBranchName,payoutStatus,payoutDate,payoutUser,payoutTicket FROM mkt2_trip_records WHERE recordId=? LIMIT 1", [$recordId]);
        if (!$current) {
            json_response(['error' => 'Registro no encontrado'], 404);
        }

        $branchCode = clean($data['assignedBranchCode'] ?? $current['assignedBranchCode'] ?? '');
        $branchName = clean($data['assignedBranchName'] ?? $current['assignedBranchName'] ?? '');
        $assignedBranch = clean($data['assignedBranch'] ?? $current['assignedBranch'] ?? trim($branchCode . ' - ' . $branchName, ' -'));
        $badgeIds = normalize_badges_input($data['badgeIds'] ?? null, $data['badgeId'] ?? $current['badgeId']);
        $badgeSummary = implode(', ', $badgeIds);
        // Si la app no manda sellerBadges (version vieja), se conserva el
        // detalle que ya estaba guardado en lugar de aplastarlo.
        $existingPairs = trip_record_sellers_map($db, [$recordId])[$recordId] ?? [];
        $sellerPairsInput = array_key_exists('sellerBadges', $data) ? $data['sellerBadges'] : $existingPairs;
        $sellerPairs = normalize_seller_badges($sellerPairsInput, $badgeIds, clean($data['sellerKey'] ?? $current['sellerKey']), clean($data['sellerName'] ?? $current['sellerName']));
        $primarySeller = primary_seller_from_badges($sellerPairs);

        save_trip_record($db, [
            'recordId' => $recordId,
            'catalogId' => array_key_exists('catalogId', $data) ? $data['catalogId'] : $current['catalogId'],
            'badgeId' => $badgeSummary,
            'driverName' => clean($data['driverName'] ?? $current['driverName']),
            'driverPhone' => clean($data['driverPhone'] ?? $current['driverPhone']),
            'contactPhone' => clean($data['contactPhone'] ?? $current['contactPhone']),
            'nationality' => clean($data['nationality'] ?? $current['nationality']),
            'plate' => clean($data['plate'] ?? $current['plate']),
            'vehicleModel' => clean($data['vehicleModel'] ?? $current['vehicleModel']),
            'unitNumber' => clean($data['unitNumber'] ?? $current['unitNumber']),
            'hotel' => clean($data['hotel'] ?? $current['hotel']),
            'origin' => clean($data['origin'] ?? $current['origin']),
            'site' => clean($data['site'] ?? $current['site']),
            'destination' => clean($data['destination'] ?? $current['destination']),
            'sellerKey' => $primarySeller['sellerKey'],
            'sellerName' => $primarySeller['sellerName'],
            'adultCount' => array_key_exists('adultCount', $data) ? non_negative_int($data['adultCount']) : non_negative_int($current['adultCount']),
            'youthCount' => array_key_exists('youthCount', $data) ? non_negative_int($data['youthCount']) : non_negative_int($current['youthCount']),
            'minorCount' => array_key_exists('minorCount', $data) ? non_negative_int($data['minorCount']) : non_negative_int($current['minorCount']),
            'noShowCount' => array_key_exists('noShowCount', $data) ? non_negative_int($data['noShowCount']) : non_negative_int($current['noShowCount']),
            'passengerCount' => array_key_exists('passengerCount', $data) ? non_negative_int($data['passengerCount']) : non_negative_int($current['passengerCount']),
            'serviceType' => clean($data['serviceType'] ?? $current['serviceType']),
            'tripCost' => array_key_exists('tripCost', $data) ? (float)$data['tripCost'] : (float)$current['tripCost'],
            'paymentMethod' => clean($data['paymentMethod'] ?? $current['paymentMethod']),
            'notes' => clean($data['notes'] ?? $current['notes']),
            'recordDate' => mysql_datetime($data['recordDate'] ?? $current['recordDate']),
            'assignedBranch' => $assignedBranch,
            'assignedBranchCode' => $branchCode,
            'assignedBranchName' => $branchName,
            'payoutStatus' => clean($current['payoutStatus']),
            'payoutDate' => $current['payoutDate'],
            'payoutUser' => clean($current['payoutUser']),
            'payoutTicket' => clean($current['payoutTicket']),
            'syncStatus' => 'pending',
            'source' => 'hostinger',
        ]);

        save_trip_record_sellers($db, $recordId, $sellerPairs, $branchCode);
        sync_taxista_badges_by_seller($db, isset($data['catalogId']) ? (int)$data['catalogId'] : (isset($current['catalogId']) ? (int)$current['catalogId'] : null), clean($data['driverName'] ?? $current['driverName']), $sellerPairs, true, $branchCode);
        $updated = row($db, "SELECT recordId,catalogId,badgeId,driverName,driverPhone,contactPhone,nationality,plate,vehicleModel,unitNumber,hotel,origin,site,destination,sellerKey,sellerName,adultCount,youthCount,minorCount,noShowCount,passengerCount,serviceType,tripCost,paymentMethod,notes,recordDate,assignedBranch,assignedBranchCode,assignedBranchName,payoutStatus,payoutDate,payoutUser,payoutTicket FROM mkt2_trip_records WHERE recordId=?", [$recordId]);
        $updated['sellerBadges'] = $sellerPairs;
        $updated['diagnostic'] = [
            'contentType' => $_SERVER['CONTENT_TYPE'] ?? '',
            'rawBody' => $rawBody,
            'receivedKeys' => array_keys($data),
            'received' => [
                'tripCost' => $data['tripCost'] ?? '**NO_RECIBIDO**',
                'driverName' => $data['driverName'] ?? '**NO_RECIBIDO**',
                'catalogId' => $data['catalogId'] ?? '**NO_RECIBIDO**',
            ],
        ];
        json_response($updated);
    }
    if ($method === 'POST' && preg_match('#^/api/taxis/registros/([^/]+)/pagar$#', $path, $m)) {
        ensure_app_schema($db);
        $recordId = clean(urldecode($m[1]));
        $data = body();
        $record = row($db, "SELECT recordId,tripCost,payoutStatus FROM mkt2_trip_records WHERE recordId=? LIMIT 1", [$recordId]);
        if (!$record) {
            json_response(['error' => 'Registro no encontrado'], 404);
        }
        if (strtolower((string)$record['payoutStatus']) === 'pagado') {
            json_response(['error' => 'Esta dejada ya esta pagada'], 409);
        }
        $paidAt = date('Y-m-d H:i:s');
        $user = clean($data['user'] ?? 'app');
        $ticket = 'TK-' . $recordId . '-' . date('YmdHis');
        exec_sql($db, "UPDATE mkt2_trip_records SET payoutStatus='pagado', payoutDate=?, payoutUser=?, payoutTicket=?, syncStatus='pending', source='hostinger' WHERE recordId=? AND payoutStatus<>'pagado'", [
            $paidAt, $user, $ticket, $recordId
        ]);
        $detail = row($db, "SELECT recordId,catalogId,badgeId,driverName,driverPhone,contactPhone,nationality,plate,vehicleModel,unitNumber,hotel,origin,site,destination,sellerKey,sellerName,adultCount,youthCount,minorCount,noShowCount,passengerCount,serviceType,tripCost,paymentMethod,notes,recordDate,assignedBranch,assignedBranchCode,assignedBranchName,payoutStatus,payoutDate,payoutUser,payoutTicket FROM mkt2_trip_records WHERE recordId=?", [$recordId]);
        if (is_array($detail)) {
            $detail = attach_trip_record_sellers($db, [$detail])[0];
        }
        json_response($detail);
    }
    if ($method === 'GET' && $path === '/api/taxis/gafetes') {
        $branchCode = request_branch_code($config);
        json_response(rows($db, "SELECT badgeId,barcode,status,cycle,taxistaId,taxistaName,createdAt FROM mkt2_gafetes WHERE " . branch_filter_sql($config, 'branchCode', $branchCode) . " ORDER BY createdAt DESC LIMIT 100", [$branchCode]));
    }
    if ($method === 'POST' && $path === '/api/taxis/gafetes/generar') {
        ensure_sync_schema($db);
        $data = body();
        $branchCode = request_branch_code($config);
        $quantity = min(200, max(1, (int)($data['quantity'] ?? 1)));
        $last = (int)scalar($db, "SELECT COALESCE(MAX(CAST(badgeId AS UNSIGNED)),0) FROM mkt2_gafetes WHERE " . branch_filter_sql($config, 'branchCode', $branchCode), [$branchCode]);
        $items = [];
        for ($i = 1; $i <= $quantity; $i++) {
            $badge = $branchCode === 'CV'
                ? 'CV-' . str_pad((string)($last + $i), 3, '0', STR_PAD_LEFT)
                : str_pad((string)($last + $i), 3, '0', STR_PAD_LEFT);
            $item = ['badgeId' => $badge, 'barcode' => $badge, 'status' => empty($data['taxistaId']) ? 'Disponible' : 'Asignado', 'cycle' => 1, 'taxistaId' => $data['taxistaId'] ?? null, 'taxistaName' => clean($data['taxistaName'] ?? ''), 'createdAt' => date('Y-m-d H:i:s'), 'syncStatus' => 'pending', 'source' => 'hostinger', 'branchCode' => $branchCode];
            upsert($db, 'mkt2_gafetes', ['badgeId', 'cycle'], $item);
            $items[] = $item;
        }
        json_response($items);
    }

    if (
        $method === 'GET'
        && (
            str_starts_with($path, '/api/pos/')
            || $path === '/api/taxis/vendedores'
        )
    ) {
        route_pos_get($db, $path);
    }
    if ($method === 'POST' && $path === '/api/taxis/vendedores/sync') {
        $data = body();
        $vendors = $data['vendors'] ?? $data['items'] ?? [];
        if (!is_array($vendors)) {
            json_response(['error' => 'vendors debe ser una lista'], 422);
        }

        ensure_plaza_vendor_catalog_schema($db);
        $upserted = 0;
        $omitted = 0;
        foreach ($vendors as $vendor) {
            if (!is_array($vendor)) {
                $omitted++;
                continue;
            }

            $name = clean($vendor['vendorName'] ?? $vendor['name'] ?? '');
            $vendorKey = plaza_vendor_key($name);
            if ($vendorKey === '') {
                $omitted++;
                continue;
            }

            upsert($db, 'mkt2_catalogo_vendedoresPlaza', ['vendorKey'], [
                'vendorKey' => $vendorKey,
                'vendorName' => $name,
                'vendorCode' => clean($vendor['vendorCode'] ?? $vendor['vendorKey'] ?? ''),
                'sourceDatabase' => pos_database($vendor['sourceDatabase'] ?? $vendor['database'] ?? null),
                'phoneNumber' => clean($vendor['phoneNumber'] ?? ''),
                'commissionPercent' => max(0, (float)($vendor['commissionPercent'] ?? 0)),
                'active' => isset($vendor['active']) ? (parse_bool($vendor['active']) ? 1 : 0) : 1,
            ]);
            $upserted++;
        }

        json_response([
            'ok' => true,
            'table' => 'mkt2_catalogo_vendedoresPlaza',
            'upserted' => $upserted,
            'omitted' => $omitted,
        ]);
    }
    if ($method === 'POST' && $path === '/api/pos/ventas') {
        $data = body();
        $saleId = 'SALE-' . date('YmdHis');
        $lines = $data['lines'] ?? [];
        $subtotal = 0; $tax = 0;
        foreach ($lines as $line) {
            $lineSubtotal = (float)($line['unitPrice'] ?? 0) * (float)($line['quantity'] ?? 0);
            $subtotal += $lineSubtotal;
            $tax += ($lineSubtotal - (float)($line['discountAmount'] ?? 0)) * (((float)($line['taxRate'] ?? 0)) / 100);
        }
        $total = $subtotal + $tax;
        upsert($db, 'mkt2_pos_sales_pending', ['saleId'], ['saleId' => $saleId, 'sourceDatabase' => pos_database(query('database')), 'folio' => $saleId, 'saleDate' => mysql_datetime($data['saleDate'] ?? null), 'subtotal' => $subtotal, 'tax' => $tax, 'total' => $total, 'commissionAmount' => 0, 'payload' => json_encode($data, JSON_UNESCAPED_UNICODE)]);
        json_response(['saleId' => $saleId, 'folio' => $saleId, 'saleDate' => date('c'), 'subtotal' => $subtotal, 'tax' => $tax, 'total' => $total, 'commissionAmount' => 0]);
    }

    json_response(['error' => 'Ruta no encontrada', 'path' => $path], 404);
}

function route_sync(PDO $db, array $config, string $method, string $path): void
{
    require_sync_token($config);
    ensure_app_schema($db);
    $branchCode = sync_branch_code($config);
    $branch = branch_by_code($config, $branchCode);
    $branchName = clean($branch['branchName'] ?? '');
    $branchLabel = clean($branch['branchLabel'] ?? branch_label($branchCode, $branchName));

    if ($method === 'GET' && $path === '/sync/pull-changes') {
        $limit = min(1000, max(1, (int)(query('limit') ?? 250)));
        $tripWhere = "source='hostinger' AND syncStatus<>'synced' AND (assignedBranchCode=?";
        $tripParams = [$branchCode];
        if ($branchCode === default_branch_code($config)) {
            $tripWhere .= " OR TRIM(assignedBranchCode)=''";
        }
        $tripWhere .= ")";
        json_response([
            'serverTime' => gmdate('c'),
            'branchCode' => $branchCode,
            'changes' => [
                'mkt2_trip_records' => attach_trip_record_sellers($db, rows($db, "SELECT recordId,catalogId,badgeId,driverName,driverPhone,contactPhone,nationality,plate,vehicleModel,unitNumber,hotel,origin,site,destination,sellerKey,sellerName,adultCount,youthCount,minorCount,noShowCount,passengerCount,serviceType,tripCost,paymentMethod,notes,recordDate,assignedBranch,assignedBranchCode,assignedBranchName,payoutStatus,payoutDate,payoutUser,payoutTicket,createdAt,updatedAt,source,syncStatus FROM mkt2_trip_records WHERE $tripWhere ORDER BY COALESCE(updatedAt, createdAt) ASC LIMIT $limit", $tripParams)),
                'mkt2_catalog_taxis' => rows($db, "SELECT catalogId,badgeId,driverName,phoneNumber,plate,vehicleModel,unitNumber,serviceType,site,hotel,notes,suggestedAmount,active,createdAt,updatedAt,source,syncStatus,branchCode FROM mkt2_catalog_taxis WHERE source='hostinger' AND syncStatus<>'synced' AND branchCode=? ORDER BY COALESCE(updatedAt, createdAt) ASC LIMIT $limit", [$branchCode]),
                'mkt2_gafetes' => rows($db, "SELECT badgeId,barcode,status,cycle,taxistaId,taxistaName,createdAt,updatedAt,source,syncStatus,branchCode FROM mkt2_gafetes WHERE source='hostinger' AND syncStatus<>'synced' AND branchCode=? ORDER BY COALESCE(updatedAt, createdAt) ASC LIMIT $limit", [$branchCode]),
                'mkt2_service_rates' => rows($db, "SELECT serviceType,description,baseAmount,extraPersonAmount,createdAt,updatedAt,source,syncStatus,branchCode FROM mkt2_service_rates WHERE source='hostinger' AND syncStatus<>'synced' AND branchCode=? ORDER BY COALESCE(updatedAt, createdAt) ASC LIMIT $limit", [$branchCode]),
            ],
        ]);
    }

    if ($method === 'POST' && $path === '/sync/push-changes') {
        $data = body();
        $result = [
            'mkt2_catalog_taxis' => push_sync_items($db, 'mkt2_catalog_taxis', ['catalogId'], [
                'catalogId','badgeId','driverName','phoneNumber','plate','vehicleModel','unitNumber','serviceType','site','hotel','notes','suggestedAmount','active'
            ], $data['mkt2_catalog_taxis'] ?? [], $branchCode),
            'mkt2_trip_records' => push_sync_items($db, 'mkt2_trip_records', ['recordId'], [
                'recordId','catalogId','badgeId','driverName','driverPhone','contactPhone','nationality','plate','vehicleModel','unitNumber','hotel','origin','site','destination','sellerKey','sellerName','adultCount','youthCount','minorCount','noShowCount','passengerCount','serviceType','tripCost','paymentMethod','notes','recordDate','assignedBranch','assignedBranchCode','assignedBranchName','payoutStatus','payoutDate','payoutUser','payoutTicket'
            ], $data['mkt2_trip_records'] ?? [], $branchCode, $branchName, $branchLabel),
            'mkt2_gafetes' => push_sync_items($db, 'mkt2_gafetes', ['badgeId', 'cycle'], [
                'badgeId','barcode','status','cycle','taxistaId','taxistaName','createdAt'
            ], $data['mkt2_gafetes'] ?? [], $branchCode),
            'mkt2_service_rates' => push_sync_items($db, 'mkt2_service_rates', ['serviceType'], [
                'serviceType','description','baseAmount','extraPersonAmount'
            ], $data['mkt2_service_rates'] ?? [], $branchCode),
        ];
        json_response(['ok' => true, 'branchCode' => $branchCode, 'upserted' => $result, 'serverTime' => gmdate('c')]);
    }

    if ($method === 'POST' && $path === '/sync/mark-synced') {
        $data = body();
        $result = [
            'mkt2_trip_records' => mark_synced($db, 'mkt2_trip_records', 'recordId', $data['mkt2_trip_records'] ?? [], $branchCode),
            'mkt2_catalog_taxis' => mark_synced($db, 'mkt2_catalog_taxis', 'catalogId', $data['mkt2_catalog_taxis'] ?? [], $branchCode),
            'mkt2_service_rates' => mark_synced($db, 'mkt2_service_rates', 'serviceType', $data['mkt2_service_rates'] ?? [], $branchCode),
            'mkt2_gafetes' => mark_gafetes_synced($db, $data['mkt2_gafetes'] ?? [], $branchCode),
        ];
        json_response(['ok' => true, 'branchCode' => $branchCode, 'marked' => $result, 'serverTime' => gmdate('c')]);
    }

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

    json_response(['error' => 'Ruta sync no encontrada', 'path' => $path], 404);
}

function route_auth(PDO $db, array $config, string $method, string $path): void
{
    ensure_app_schema($db);

    if ($method === 'GET' && $path === '/api/auth/branches') {
        json_response(branches($db, $config));
    }

    if ($method === 'POST' && $path === '/api/auth/login') {
        $data = body();
        $username = clean($data['username'] ?? '');
        $password = (string)($data['password'] ?? '');
        $user = row($db, "SELECT userId,username,passwordHash,fullName,role,branchCode,branchName,branchLabel,active,canViewHome,canViewRegister,canViewTaxistas,canViewHistory,canViewUsers,canViewAllBranches FROM mkt2_users WHERE username=? LIMIT 1", [$username]);
        if (!$user || !(bool)$user['active'] || !password_verify($password, (string)$user['passwordHash'])) {
            json_response(['error' => 'Usuario o contrasena incorrectos'], 401);
        }
        unset($user['passwordHash']);
        json_response(['user' => normalize_user($user)]);
    }

    if ($method === 'GET' && $path === '/api/auth/users') {
        json_response(rows($db, "SELECT userId,username,fullName,role,branchCode,branchName,branchLabel,active,canViewHome,canViewRegister,canViewTaxistas,canViewHistory,canViewUsers,canViewAllBranches,createdAt,updatedAt FROM mkt2_users ORDER BY active DESC, username"));
    }

    if ($method === 'POST' && $path === '/api/auth/users') {
        $data = body();
        $password = (string)($data['password'] ?? '');
        if (clean($data['username'] ?? '') === '' || $password === '') {
            json_response(['error' => 'Usuario y contrasena son obligatorios'], 422);
        }
        $branch = branch_label(clean($data['branchCode'] ?? ''), clean($data['branchName'] ?? ''));
        exec_sql($db, "INSERT INTO mkt2_users (username,passwordHash,fullName,role,branchCode,branchName,branchLabel,active,canViewHome,canViewRegister,canViewTaxistas,canViewHistory,canViewUsers,canViewAllBranches) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?)", [
            clean($data['username'] ?? ''),
            password_hash($password, PASSWORD_DEFAULT),
            clean($data['fullName'] ?? ''),
            clean($data['role'] ?? 'operador') ?: 'operador',
            clean($data['branchCode'] ?? ''),
            clean($data['branchName'] ?? ''),
            $branch,
            parse_bool($data['active'] ?? true) ? 1 : 0,
            parse_bool($data['canViewHome'] ?? true) ? 1 : 0,
            parse_bool($data['canViewRegister'] ?? true) ? 1 : 0,
            parse_bool($data['canViewTaxistas'] ?? true) ? 1 : 0,
            parse_bool($data['canViewHistory'] ?? true) ? 1 : 0,
            parse_bool($data['canViewUsers'] ?? false) ? 1 : 0,
            parse_bool($data['canViewAllBranches'] ?? false) ? 1 : 0,
        ]);
        json_response(row($db, "SELECT userId,username,fullName,role,branchCode,branchName,branchLabel,active,canViewHome,canViewRegister,canViewTaxistas,canViewHistory,canViewUsers,canViewAllBranches,createdAt,updatedAt FROM mkt2_users WHERE userId=LAST_INSERT_ID()"));
    }

    if ($method === 'PUT' && preg_match('#^/api/auth/users/(\d+)$#', $path, $m)) {
        $data = body();
        $id = (int)$m[1];
        $branch = branch_label(clean($data['branchCode'] ?? ''), clean($data['branchName'] ?? ''));
        exec_sql($db, "UPDATE mkt2_users SET username=?,fullName=?,role=?,branchCode=?,branchName=?,branchLabel=?,active=?,canViewHome=?,canViewRegister=?,canViewTaxistas=?,canViewHistory=?,canViewUsers=?,canViewAllBranches=? WHERE userId=?", [
            clean($data['username'] ?? ''),
            clean($data['fullName'] ?? ''),
            clean($data['role'] ?? 'operador') ?: 'operador',
            clean($data['branchCode'] ?? ''),
            clean($data['branchName'] ?? ''),
            $branch,
            parse_bool($data['active'] ?? true) ? 1 : 0,
            parse_bool($data['canViewHome'] ?? true) ? 1 : 0,
            parse_bool($data['canViewRegister'] ?? true) ? 1 : 0,
            parse_bool($data['canViewTaxistas'] ?? true) ? 1 : 0,
            parse_bool($data['canViewHistory'] ?? true) ? 1 : 0,
            parse_bool($data['canViewUsers'] ?? false) ? 1 : 0,
            parse_bool($data['canViewAllBranches'] ?? false) ? 1 : 0,
            $id,
        ]);
        if (clean($data['password'] ?? '') !== '') {
            exec_sql($db, "UPDATE mkt2_users SET passwordHash=? WHERE userId=?", [password_hash((string)$data['password'], PASSWORD_DEFAULT), $id]);
        }
        json_response(row($db, "SELECT userId,username,fullName,role,branchCode,branchName,branchLabel,active,canViewHome,canViewRegister,canViewTaxistas,canViewHistory,canViewUsers,canViewAllBranches,createdAt,updatedAt FROM mkt2_users WHERE userId=?", [$id]));
    }

    if ($method === 'DELETE' && preg_match('#^/api/auth/users/(\d+)$#', $path, $m)) {
        exec_sql($db, "UPDATE mkt2_users SET active=0 WHERE userId=?", [(int)$m[1]]);
        json_response(['deleted' => true]);
    }

    json_response(['error' => 'Ruta auth no encontrada', 'path' => $path], 404);
}

function branches(PDO $db, array $config): array
{
    $configured = configured_branches($config);
    if ($configured !== []) return $configured;

    $table = branch_source_table($db);
    if ($table === null) return [];
    return rows($db, "SELECT CAST(almacen AS CHAR) AS branchCode, TRIM(Nombre) AS branchName, CONCAT(CAST(almacen AS CHAR), ' - ', TRIM(Nombre)) AS branchLabel FROM `$table` WHERE TRIM(CAST(almacen AS CHAR))<>'' AND TRIM(Nombre)<>'' ORDER BY CAST(almacen AS UNSIGNED), Nombre");
}

function configured_branches(array $config): array
{
    $branches = $config['branches'] ?? [];
    if (!is_array($branches)) return [];

    $items = [];
    foreach ($branches as $branch) {
        if (!is_array($branch)) continue;
        if (array_key_exists('enabled', $branch) && !$branch['enabled']) continue;

        $code = clean($branch['branchCode'] ?? '');
        $name = clean($branch['branchName'] ?? '');
        $label = clean($branch['branchLabel'] ?? branch_label($code, $name));
        if ($code === '' && $name === '') continue;

        $items[] = [
            'branchCode' => $code,
            'branchName' => $name,
            'branchLabel' => $label,
            'serverKey' => clean($branch['serverKey'] ?? ''),
            'serverName' => clean($branch['serverName'] ?? ''),
        ];
    }

    return $items;
}

function branch_source_table(PDO $db): ?string
{
    foreach (['compuadmo_almacenes', 'compuadmo_almacen', 'almacenes'] as $table) {
        $exists = (int)scalar($db, "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=?", [$table]);
        if ($exists > 0) return $table;
    }
    return null;
}

function branch_label(string $code, string $name): string
{
    if ($code !== '' && $name !== '') return "$code - $name";
    return $code !== '' ? $code : $name;
}

function branch_by_code(array $config, string $code): ?array
{
    foreach (configured_branches($config) as $branch) {
        if (strcasecmp((string)$branch['branchCode'], $code) === 0) {
            return $branch;
        }
    }
    return null;
}

function default_branch_code(array $config): string
{
    $branches = configured_branches($config);
    return clean($branches[0]['branchCode'] ?? '28') ?: '28';
}

function request_branch_code(array $config): string
{
    $code = clean(query('branchCode') ?? ($_SERVER['HTTP_X_BRANCH_CODE'] ?? ''));
    if ($code === '') {
        return default_branch_code($config);
    }
    $branch = branch_by_code($config, $code);
    return $branch ? clean($branch['branchCode'] ?? $code) : default_branch_code($config);
}

function branch_filter_sql(array $config, string $column, string $branchCode): string
{
    $default = default_branch_code($config);
    if (strcasecmp($branchCode, $default) === 0) {
        return "($column=? OR TRIM(COALESCE($column, ''))='')";
    }
    return "$column=?";
}

function sync_branch_code(array $config): string
{
    $code = clean(query('branchCode') ?? ($_SERVER['HTTP_X_BRANCH_CODE'] ?? ''));
    if ($code === '') {
        $code = default_branch_code($config);
    }
    if (branch_by_code($config, $code) === null) {
        json_response(['error' => 'Sucursal no configurada para sincronizar', 'branchCode' => $code], 422);
    }
    return $code;
}

function normalize_user(array $user): array
{
    $user['userId'] = (int)($user['userId'] ?? 0);
    $user['active'] = (bool)($user['active'] ?? false);
    return $user;
}

function require_sync_token(array $config): void
{
    $expected = (string)($config['sync_token'] ?? '');
    $provided = (string)($_SERVER['HTTP_X_SYNC_TOKEN'] ?? '');
    if ($expected === '' || !hash_equals($expected, $provided)) {
        json_response(['error' => 'No autorizado'], 401);
    }
}

function push_sync_items(PDO $db, string $table, array $keys, array $columns, mixed $items, string $branchCode, string $branchName = '', string $branchLabel = ''): int
{
    if (!is_array($items)) return 0;
    $count = 0;
    foreach ($items as $item) {
        if (!is_array($item)) continue;
        $row = [];
        foreach ($columns as $column) {
            $row[$column] = sync_value($column, $item[$column] ?? null);
        }
        if ($table === 'mkt2_trip_records') {
            if (clean($row['assignedBranchCode'] ?? '') === '') {
                $row['assignedBranchCode'] = $branchCode;
            }
            if (clean($row['assignedBranchName'] ?? '') === '') {
                $row['assignedBranchName'] = $branchName;
            }
            if (clean($row['assignedBranch'] ?? '') === '') {
                $row['assignedBranch'] = $branchLabel !== '' ? $branchLabel : branch_label($branchCode, $branchName);
            }
        } else {
            $row['branchCode'] = $branchCode;
        }
        $row['syncStatus'] = 'synced';
        $row['syncedAt'] = date('Y-m-d H:i:s');
        $row['source'] = 'sqlserver';
        if ($table === 'mkt2_trip_records') {
            save_trip_record($db, $row);
        } else {
            upsert($db, $table, $keys, $row);
        }
        $count++;
    }
    return $count;
}

function save_trip_record(PDO $db, array $row): void
{
    $recordId = clean($row['recordId'] ?? '');
    if ($recordId === '') {
        throw new InvalidArgumentException('recordId es requerido para guardar mkt2_trip_records');
    }

    $exists = (int)scalar($db, "SELECT COUNT(*) FROM mkt2_trip_records WHERE recordId=?", [$recordId]) > 0;
    if (!$exists) {
        upsert($db, 'mkt2_trip_records', ['recordId'], $row);
        return;
    }

    exec_sql($db, "UPDATE mkt2_trip_records
        SET catalogId=?,
            badgeId=?,
            driverName=?,
            driverPhone=?,
            contactPhone=?,
            nationality=?,
            plate=?,
            vehicleModel=?,
            unitNumber=?,
            hotel=?,
            origin=?,
            site=?,
            destination=?,
            sellerKey=?,
            sellerName=?,
            adultCount=?,
            youthCount=?,
            minorCount=?,
            noShowCount=?,
            passengerCount=?,
            serviceType=?,
            tripCost=?,
            paymentMethod=?,
            notes=?,
            recordDate=?,
            assignedBranch=?,
            assignedBranchCode=?,
            assignedBranchName=?,
            payoutStatus=?,
            payoutDate=?,
            payoutUser=?,
            payoutTicket=?,
            syncStatus=?,
            syncedAt=?,
            source=?
        WHERE recordId=?", [
        $row['catalogId'] ?? null,
        $row['badgeId'] ?? '',
        clean($row['driverName'] ?? ''),
        clean($row['driverPhone'] ?? ''),
        clean($row['contactPhone'] ?? ''),
        clean($row['nationality'] ?? ''),
        clean($row['plate'] ?? ''),
        clean($row['vehicleModel'] ?? ''),
        clean($row['unitNumber'] ?? ''),
        clean($row['hotel'] ?? ''),
        clean($row['origin'] ?? ''),
        clean($row['site'] ?? ''),
        clean($row['destination'] ?? ''),
        clean($row['sellerKey'] ?? ''),
        clean($row['sellerName'] ?? ''),
        (int)($row['adultCount'] ?? 0),
        (int)($row['youthCount'] ?? 0),
        (int)($row['minorCount'] ?? 0),
        (int)($row['noShowCount'] ?? 0),
        (int)($row['passengerCount'] ?? 0),
        clean($row['serviceType'] ?? ''),
        $row['tripCost'] ?? 0,
        clean($row['paymentMethod'] ?? ''),
        clean($row['notes'] ?? ''),
        $row['recordDate'] ?? null,
        clean($row['assignedBranch'] ?? ''),
        clean($row['assignedBranchCode'] ?? ''),
        clean($row['assignedBranchName'] ?? ''),
        clean($row['payoutStatus'] ?? 'pendiente'),
        $row['payoutDate'] ?? null,
        clean($row['payoutUser'] ?? ''),
        clean($row['payoutTicket'] ?? ''),
        clean($row['syncStatus'] ?? 'synced'),
        $row['syncedAt'] ?? null,
        clean($row['source'] ?? 'sqlserver'),
        $recordId,
    ]);
}

function sync_value(string $column, mixed $value): mixed
{
    if ($value === null) return null;
    if (in_array($column, ['catalogId', 'adultCount', 'youthCount', 'minorCount', 'noShowCount', 'passengerCount', 'cycle', 'taxistaId'], true)) return (int)$value;
    if (in_array($column, ['suggestedAmount', 'tripCost', 'baseAmount', 'extraPersonAmount'], true)) return (float)$value;
    if (in_array($column, ['active'], true)) return parse_bool($value) ? 1 : 0;
    if (str_ends_with(strtolower($column), 'date') || in_array($column, ['createdAt'], true)) {
        $time = strtotime((string)$value);
        return $time ? date('Y-m-d H:i:s', $time) : null;
    }
    return clean($value);
}

function mark_synced(PDO $db, string $table, string $key, mixed $ids, string $branchCode): int
{
    if (!is_array($ids)) return 0;
    $count = 0;
    foreach ($ids as $id) {
        if ($table === 'mkt2_trip_records') {
            exec_sql($db, "UPDATE `$table` SET syncStatus='synced', syncedAt=NOW() WHERE `$key`=? AND (assignedBranchCode=? OR TRIM(assignedBranchCode)='')", [$id, $branchCode]);
        } else {
            exec_sql($db, "UPDATE `$table` SET syncStatus='synced', syncedAt=NOW() WHERE `$key`=? AND branchCode=?", [$id, $branchCode]);
        }
        $count++;
    }
    return $count;
}

function mark_gafetes_synced(PDO $db, mixed $items, string $branchCode): int
{
    if (!is_array($items)) return 0;
    $count = 0;
    foreach ($items as $item) {
        if (is_array($item)) {
            exec_sql($db, "UPDATE mkt2_gafetes SET syncStatus='synced', syncedAt=NOW() WHERE badgeId=? AND cycle=? AND branchCode=?", [$item['badgeId'] ?? '', (int)($item['cycle'] ?? 1), $branchCode]);
        } else {
            exec_sql($db, "UPDATE mkt2_gafetes SET syncStatus='synced', syncedAt=NOW() WHERE badgeId=? AND branchCode=?", [$item, $branchCode]);
        }
        $count++;
    }
    return $count;
}

function ensure_sync_schema(PDO $db): void
{
    static $done = false;
    if ($done) return;
    ensure_catalog_schema($db);
    exec_sql($db, "CREATE TABLE IF NOT EXISTS mkt2_gafetes (
        badgeId VARCHAR(80) NOT NULL,
        barcode VARCHAR(120) NOT NULL DEFAULT '',
        status VARCHAR(30) NOT NULL DEFAULT 'Disponible',
        cycle INT NOT NULL DEFAULT 1,
        taxistaId INT NULL,
        taxistaName VARCHAR(180) NOT NULL DEFAULT '',
        createdAt DATETIME NULL DEFAULT CURRENT_TIMESTAMP,
        updatedAt TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
        syncStatus VARCHAR(20) NOT NULL DEFAULT 'synced',
        syncedAt DATETIME NULL,
        source VARCHAR(40) NOT NULL DEFAULT 'sqlserver',
        branchCode VARCHAR(50) NOT NULL DEFAULT '28',
        PRIMARY KEY (badgeId, cycle),
        INDEX ix_gafetes_taxista (taxistaId, badgeId)
    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
    // Vendedor que entrego el gafete (quien hizo el registro en la app), no
    // solo el taxista que lo recibio. Se guarda aparte porque el vendedor de
    // la venta y el taxista del viaje son personas distintas.
    ensure_column($db, 'mkt2_gafetes', 'vendorKey', "ALTER TABLE mkt2_gafetes ADD COLUMN vendorKey VARCHAR(80) NOT NULL DEFAULT ''");
    // Una llegada la pueden atender varios vendedores: uno por cada gafete
    // entregado, y un mismo vendedor puede quedarse con varios gafetes. La
    // columna sellerKey/sellerName de mkt2_trip_records solo guarda al primero,
    // por eso el detalle vive en su propia tabla.
    exec_sql($db, "CREATE TABLE IF NOT EXISTS mkt2_trip_record_sellers (
        recordId VARCHAR(80) NOT NULL,
        badgeId VARCHAR(80) NOT NULL,
        sellerKey VARCHAR(80) NOT NULL DEFAULT '',
        sellerName VARCHAR(180) NOT NULL DEFAULT '',
        branchCode VARCHAR(50) NOT NULL DEFAULT '',
        createdAt DATETIME NULL DEFAULT CURRENT_TIMESTAMP,
        updatedAt TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
        syncStatus VARCHAR(20) NOT NULL DEFAULT 'pending',
        source VARCHAR(40) NOT NULL DEFAULT 'hostinger',
        PRIMARY KEY (recordId, badgeId),
        INDEX ix_record_sellers_seller (sellerKey, sellerName)
    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
    ensure_column($db, 'mkt2_gafetes', 'vendorName', "ALTER TABLE mkt2_gafetes ADD COLUMN vendorName VARCHAR(180) NOT NULL DEFAULT ''");
    foreach (['mkt2_catalog_taxis', 'mkt2_trip_records', 'mkt2_gafetes', 'mkt2_service_rates'] as $table) {
        ensure_column($db, $table, 'syncStatus', "ALTER TABLE `$table` ADD COLUMN syncStatus VARCHAR(20) NOT NULL DEFAULT 'synced'");
        ensure_column($db, $table, 'syncedAt', "ALTER TABLE `$table` ADD COLUMN syncedAt DATETIME NULL");
        ensure_column($db, $table, 'createdAt', "ALTER TABLE `$table` ADD COLUMN createdAt DATETIME NULL DEFAULT CURRENT_TIMESTAMP");
        ensure_column($db, $table, 'source', "ALTER TABLE `$table` ADD COLUMN source VARCHAR(40) NOT NULL DEFAULT 'sqlserver'");
    }
    foreach (['mkt2_catalog_taxis', 'mkt2_gafetes', 'mkt2_service_rates'] as $table) {
        ensure_column($db, $table, 'branchCode', "ALTER TABLE `$table` ADD COLUMN branchCode VARCHAR(50) NOT NULL DEFAULT '28'");
    }
    $done = true;
}

function ensure_catalog_schema(PDO $db): void
{
    static $done = false;
    if ($done) return;
    exec_sql($db, "CREATE TABLE IF NOT EXISTS mkt2_catalog_taxis (
        catalogId INT NOT NULL PRIMARY KEY,
        badgeId VARCHAR(50) NOT NULL DEFAULT '',
        driverName VARCHAR(180) NOT NULL DEFAULT '',
        phoneNumber VARCHAR(60) NOT NULL DEFAULT '',
        plate VARCHAR(80) NOT NULL DEFAULT '',
        vehicleModel VARCHAR(180) NOT NULL DEFAULT '',
        unitNumber VARCHAR(80) NOT NULL DEFAULT '',
        serviceType VARCHAR(120) NOT NULL DEFAULT '',
        site VARCHAR(180) NOT NULL DEFAULT '',
        hotel VARCHAR(220) NOT NULL DEFAULT '',
        notes VARCHAR(700) NOT NULL DEFAULT '',
        suggestedAmount DECIMAL(18,2) NOT NULL DEFAULT 0,
        active TINYINT(1) NOT NULL DEFAULT 1,
        createdAt DATETIME NULL DEFAULT CURRENT_TIMESTAMP,
        updatedAt TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
        syncStatus VARCHAR(20) NOT NULL DEFAULT 'synced',
        syncedAt DATETIME NULL,
        source VARCHAR(40) NOT NULL DEFAULT 'sqlserver',
        branchCode VARCHAR(50) NOT NULL DEFAULT '28',
        INDEX ix_catalog_search (active, driverName, badgeId, plate, unitNumber)
    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
    ensure_column($db, 'mkt2_catalog_taxis', 'badgeId', "ALTER TABLE mkt2_catalog_taxis ADD COLUMN badgeId VARCHAR(50) NOT NULL DEFAULT ''");
    ensure_column($db, 'mkt2_catalog_taxis', 'driverName', "ALTER TABLE mkt2_catalog_taxis ADD COLUMN driverName VARCHAR(180) NOT NULL DEFAULT ''");
    ensure_column($db, 'mkt2_catalog_taxis', 'phoneNumber', "ALTER TABLE mkt2_catalog_taxis ADD COLUMN phoneNumber VARCHAR(60) NOT NULL DEFAULT ''");
    ensure_column($db, 'mkt2_catalog_taxis', 'plate', "ALTER TABLE mkt2_catalog_taxis ADD COLUMN plate VARCHAR(80) NOT NULL DEFAULT ''");
    ensure_column($db, 'mkt2_catalog_taxis', 'vehicleModel', "ALTER TABLE mkt2_catalog_taxis ADD COLUMN vehicleModel VARCHAR(180) NOT NULL DEFAULT ''");
    ensure_column($db, 'mkt2_catalog_taxis', 'unitNumber', "ALTER TABLE mkt2_catalog_taxis ADD COLUMN unitNumber VARCHAR(80) NOT NULL DEFAULT ''");
    ensure_column($db, 'mkt2_catalog_taxis', 'serviceType', "ALTER TABLE mkt2_catalog_taxis ADD COLUMN serviceType VARCHAR(120) NOT NULL DEFAULT ''");
    ensure_column($db, 'mkt2_catalog_taxis', 'site', "ALTER TABLE mkt2_catalog_taxis ADD COLUMN site VARCHAR(180) NOT NULL DEFAULT ''");
    ensure_column($db, 'mkt2_catalog_taxis', 'hotel', "ALTER TABLE mkt2_catalog_taxis ADD COLUMN hotel VARCHAR(220) NOT NULL DEFAULT ''");
    ensure_column($db, 'mkt2_catalog_taxis', 'notes', "ALTER TABLE mkt2_catalog_taxis ADD COLUMN notes VARCHAR(700) NOT NULL DEFAULT ''");
    ensure_column($db, 'mkt2_catalog_taxis', 'suggestedAmount', "ALTER TABLE mkt2_catalog_taxis ADD COLUMN suggestedAmount DECIMAL(18,2) NOT NULL DEFAULT 0");
    ensure_column($db, 'mkt2_catalog_taxis', 'active', "ALTER TABLE mkt2_catalog_taxis ADD COLUMN active TINYINT(1) NOT NULL DEFAULT 1");
    ensure_column($db, 'mkt2_catalog_taxis', 'createdAt', "ALTER TABLE mkt2_catalog_taxis ADD COLUMN createdAt DATETIME NULL DEFAULT CURRENT_TIMESTAMP");
    ensure_column($db, 'mkt2_catalog_taxis', 'syncStatus', "ALTER TABLE mkt2_catalog_taxis ADD COLUMN syncStatus VARCHAR(20) NOT NULL DEFAULT 'synced'");
    ensure_column($db, 'mkt2_catalog_taxis', 'syncedAt', "ALTER TABLE mkt2_catalog_taxis ADD COLUMN syncedAt DATETIME NULL");
    ensure_column($db, 'mkt2_catalog_taxis', 'source', "ALTER TABLE mkt2_catalog_taxis ADD COLUMN source VARCHAR(40) NOT NULL DEFAULT 'sqlserver'");
    ensure_column($db, 'mkt2_catalog_taxis', 'branchCode', "ALTER TABLE mkt2_catalog_taxis ADD COLUMN branchCode VARCHAR(50) NOT NULL DEFAULT '28'");
    $done = true;
}

function ensure_app_schema(PDO $db): void
{
    ensure_sync_schema($db);
    exec_sql($db, "ALTER TABLE mkt2_trip_records MODIFY COLUMN badgeId VARCHAR(300) NOT NULL DEFAULT ''");
    ensure_column($db, 'mkt2_trip_records', 'nationality', "ALTER TABLE mkt2_trip_records ADD COLUMN nationality VARCHAR(120) NOT NULL DEFAULT ''");
    ensure_column($db, 'mkt2_trip_records', 'assignedBranch', "ALTER TABLE mkt2_trip_records ADD COLUMN assignedBranch VARCHAR(220) NOT NULL DEFAULT ''");
    ensure_column($db, 'mkt2_trip_records', 'assignedBranchCode', "ALTER TABLE mkt2_trip_records ADD COLUMN assignedBranchCode VARCHAR(50) NOT NULL DEFAULT ''");
    ensure_column($db, 'mkt2_trip_records', 'assignedBranchName', "ALTER TABLE mkt2_trip_records ADD COLUMN assignedBranchName VARCHAR(180) NOT NULL DEFAULT ''");
    ensure_column($db, 'mkt2_trip_records', 'sellerKey', "ALTER TABLE mkt2_trip_records ADD COLUMN sellerKey VARCHAR(80) NOT NULL DEFAULT ''");
    ensure_column($db, 'mkt2_trip_records', 'sellerName', "ALTER TABLE mkt2_trip_records ADD COLUMN sellerName VARCHAR(180) NOT NULL DEFAULT ''");
    ensure_column($db, 'mkt2_trip_records', 'adultCount', "ALTER TABLE mkt2_trip_records ADD COLUMN adultCount INT NOT NULL DEFAULT 0");
    ensure_column($db, 'mkt2_trip_records', 'youthCount', "ALTER TABLE mkt2_trip_records ADD COLUMN youthCount INT NOT NULL DEFAULT 0");
    ensure_column($db, 'mkt2_trip_records', 'minorCount', "ALTER TABLE mkt2_trip_records ADD COLUMN minorCount INT NOT NULL DEFAULT 0");
    ensure_column($db, 'mkt2_trip_records', 'noShowCount', "ALTER TABLE mkt2_trip_records ADD COLUMN noShowCount INT NOT NULL DEFAULT 0");
    ensure_column($db, 'mkt2_trip_records', 'payoutStatus', "ALTER TABLE mkt2_trip_records ADD COLUMN payoutStatus VARCHAR(20) NOT NULL DEFAULT 'pendiente'");
    ensure_column($db, 'mkt2_trip_records', 'payoutDate', "ALTER TABLE mkt2_trip_records ADD COLUMN payoutDate DATETIME NULL");
    ensure_column($db, 'mkt2_trip_records', 'payoutUser', "ALTER TABLE mkt2_trip_records ADD COLUMN payoutUser VARCHAR(180) NOT NULL DEFAULT ''");
    ensure_column($db, 'mkt2_trip_records', 'payoutTicket', "ALTER TABLE mkt2_trip_records ADD COLUMN payoutTicket VARCHAR(80) NOT NULL DEFAULT ''");
    exec_sql($db, "CREATE TABLE IF NOT EXISTS mkt2_trip_counters (
        counterName VARCHAR(40) NOT NULL PRIMARY KEY,
        lastNumber INT NOT NULL DEFAULT 0
    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
    exec_sql($db, "CREATE TABLE IF NOT EXISTS mkt2_users (
        userId INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
        username VARCHAR(80) NOT NULL UNIQUE,
        passwordHash VARCHAR(255) NOT NULL,
        fullName VARCHAR(180) NOT NULL DEFAULT '',
        role VARCHAR(30) NOT NULL DEFAULT 'operador',
        branchCode VARCHAR(50) NOT NULL DEFAULT '',
        branchName VARCHAR(180) NOT NULL DEFAULT '',
        branchLabel VARCHAR(220) NOT NULL DEFAULT '',
        active TINYINT(1) NOT NULL DEFAULT 1,
        canViewHome TINYINT(1) NOT NULL DEFAULT 1,
        canViewRegister TINYINT(1) NOT NULL DEFAULT 1,
        canViewTaxistas TINYINT(1) NOT NULL DEFAULT 1,
        canViewHistory TINYINT(1) NOT NULL DEFAULT 1,
        canViewUsers TINYINT(1) NOT NULL DEFAULT 0,
        canViewAllBranches TINYINT(1) NOT NULL DEFAULT 0,
        createdAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
        updatedAt TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
    ensure_column($db, 'mkt2_users', 'canViewHome', "ALTER TABLE mkt2_users ADD COLUMN canViewHome TINYINT(1) NOT NULL DEFAULT 1");
    ensure_column($db, 'mkt2_users', 'canViewRegister', "ALTER TABLE mkt2_users ADD COLUMN canViewRegister TINYINT(1) NOT NULL DEFAULT 1");
    ensure_column($db, 'mkt2_users', 'canViewTaxistas', "ALTER TABLE mkt2_users ADD COLUMN canViewTaxistas TINYINT(1) NOT NULL DEFAULT 1");
    ensure_column($db, 'mkt2_users', 'canViewHistory', "ALTER TABLE mkt2_users ADD COLUMN canViewHistory TINYINT(1) NOT NULL DEFAULT 1");
    ensure_column($db, 'mkt2_users', 'canViewUsers', "ALTER TABLE mkt2_users ADD COLUMN canViewUsers TINYINT(1) NOT NULL DEFAULT 0");
    ensure_column($db, 'mkt2_users', 'canViewAllBranches', "ALTER TABLE mkt2_users ADD COLUMN canViewAllBranches TINYINT(1) NOT NULL DEFAULT 0");
    $guadalupe = row($db, "SELECT userId,passwordHash FROM mkt2_users WHERE username=? LIMIT 1", ['Guadalupe']);
    if (!$guadalupe) {
        exec_sql($db, "INSERT INTO mkt2_users (username,passwordHash,fullName,role,active,canViewHome,canViewRegister,canViewTaxistas,canViewHistory,canViewUsers,canViewAllBranches) VALUES (?,?,?,?,1,1,1,1,1,1,1)", [
            'Guadalupe',
            password_hash('280625', PASSWORD_DEFAULT),
            'Guadalupe',
            'admin',
        ]);
    } elseif (password_verify('2280625', (string)$guadalupe['passwordHash'])) {
        exec_sql($db, "UPDATE mkt2_users SET passwordHash=?, role='admin', active=1, canViewHome=1, canViewRegister=1, canViewTaxistas=1, canViewHistory=1, canViewUsers=1, canViewAllBranches=1 WHERE userId=?", [
            password_hash('280625', PASSWORD_DEFAULT),
            (int)$guadalupe['userId'],
        ]);
    }
}

function ensure_dejadas_schema(PDO $db): void
{
    exec_sql($db, "CREATE TABLE IF NOT EXISTS mkt2_dejadas_taxistas_recientes (
        recentId INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
        driverName VARCHAR(180) NOT NULL,
        driverKey VARCHAR(220) NOT NULL UNIQUE,
        unitNumber VARCHAR(80) NOT NULL DEFAULT '',
        phoneNumber VARCHAR(60) NOT NULL DEFAULT '',
        transportType VARCHAR(120) NOT NULL DEFAULT '',
        site VARCHAR(180) NOT NULL DEFAULT '',
        origin VARCHAR(180) NOT NULL DEFAULT '',
        lastFolio VARCHAR(80) NOT NULL DEFAULT '',
        lastDate DATE NULL,
        sourceSheet VARCHAR(80) NOT NULL DEFAULT '',
        suggestedAmount DECIMAL(18,2) NOT NULL DEFAULT 0,
        active TINYINT(1) NOT NULL DEFAULT 1,
        createdAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
        updatedAt TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
    ensure_column($db, 'mkt2_dejadas_taxistas_recientes', 'suggestedAmount', "ALTER TABLE mkt2_dejadas_taxistas_recientes ADD COLUMN suggestedAmount DECIMAL(18,2) NOT NULL DEFAULT 0");
}

function seed_catalog_from_recent_dejadas(PDO $db): void
{
    ensure_dejadas_schema($db);
    exec_sql($db, "INSERT INTO mkt2_catalog_taxis
        (catalogId,badgeId,driverName,phoneNumber,plate,vehicleModel,unitNumber,serviceType,site,hotel,notes,suggestedAmount,active,syncStatus,source)
        SELECT
            900000000 + r.recentId,
            r.lastFolio,
            r.driverName,
            r.phoneNumber,
            CASE WHEN r.unitNumber REGEXP '[[:alpha:]]' AND r.unitNumber LIKE '%-%' THEN r.unitNumber ELSE '' END,
            '',
            r.unitNumber,
            r.transportType,
            r.site,
            r.origin,
            '',
            r.suggestedAmount,
            r.active,
            'pending',
            'hostinger'
        FROM mkt2_dejadas_taxistas_recientes r
        WHERE r.active=1
          AND TRIM(r.driverName)<>''
          AND NOT EXISTS
          (
              SELECT 1
              FROM mkt2_catalog_taxis c
              WHERE UPPER(TRIM(c.driverName))=UPPER(TRIM(r.driverName))
                AND COALESCE(NULLIF(TRIM(c.phoneNumber), ''), 'S/N')=COALESCE(NULLIF(TRIM(r.phoneNumber), ''), 'S/N')
                AND COALESCE(NULLIF(TRIM(c.unitNumber), ''), 'S/N')=COALESCE(NULLIF(TRIM(r.unitNumber), ''), 'S/N')
          )
        ON DUPLICATE KEY UPDATE
            badgeId=VALUES(badgeId),
            driverName=VALUES(driverName),
            phoneNumber=VALUES(phoneNumber),
            plate=VALUES(plate),
            unitNumber=VALUES(unitNumber),
            serviceType=VALUES(serviceType),
            site=VALUES(site),
            hotel=VALUES(hotel),
            notes=VALUES(notes),
            active=VALUES(active),
            syncStatus='pending',
            source='hostinger'");
}

function taxistas_source_sql(): string
{
    return "
        SELECT
            900000000 + r.recentId AS catalogId,
            r.lastFolio AS badgeId,
            r.driverName,
            r.phoneNumber,
            CASE WHEN r.unitNumber REGEXP '[[:alpha:]]' AND r.unitNumber LIKE '%-%' THEN r.unitNumber ELSE '' END AS plate,
            '' AS vehicleModel,
            r.unitNumber,
            r.transportType AS serviceType,
            r.site,
            r.origin AS hotel,
            '' AS notes,
            r.active,
            COALESCE(r.lastDate, DATE('1000-01-01')) AS sortLastDate,
            CAST(NULLIF(r.lastFolio, '') AS UNSIGNED) AS sortLastFolio,
            0 AS sourceRank
        FROM mkt2_dejadas_taxistas_recientes r
        WHERE TRIM(r.driverName)<>''

        UNION ALL

        SELECT
            c.catalogId,
            c.badgeId,
            c.driverName,
            c.phoneNumber,
            c.plate,
            c.vehicleModel,
            c.unitNumber,
            c.serviceType,
            c.site,
            c.hotel,
            c.notes,
            c.active,
            DATE('1000-01-01') AS sortLastDate,
            CAST(NULLIF(c.badgeId, '') AS UNSIGNED) AS sortLastFolio,
            1 AS sourceRank
        FROM mkt2_catalog_taxis c
        WHERE NOT EXISTS (
            SELECT 1
            FROM mkt2_dejadas_taxistas_recientes r2
            WHERE UPPER(TRIM(r2.driverName))=UPPER(TRIM(c.driverName))
              AND COALESCE(NULLIF(TRIM(r2.phoneNumber), ''), 'S/N')=COALESCE(NULLIF(TRIM(c.phoneNumber), ''), 'S/N')
              AND COALESCE(NULLIF(TRIM(r2.unitNumber), ''), 'S/N')=COALESCE(NULLIF(TRIM(c.unitNumber), ''), 'S/N')
        )
    ";
}

function ensure_column(PDO $db, string $table, string $column, string $sql): void
{
    $exists = (int)scalar($db, "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=? AND COLUMN_NAME=?", [$table, $column]);
    if ($exists === 0) {
        exec_sql($db, $sql);
    }
}

function route_pos_get(PDO $db, string $path): void
{
    $database = pos_database(query('database'));
    if ($path === '/api/pos/dashboard') {
        $date = date_param(query('date') ?: date('Y-m-d'));
        json_response([
            'operationsCount' => (int)scalar($db, "SELECT COUNT(*) FROM mkt2_pos_operations WHERE sourceDatabase=? AND DATE(operationDate)=?", [$database, $date]),
            'ticketsCount' => (int)scalar($db, "SELECT COUNT(*) FROM mkt2_pos_operations WHERE sourceDatabase=? AND DATE(operationDate)=?", [$database, $date]),
            'artesaniasTotal' => (float)scalar($db, "SELECT COALESCE(SUM(total),0) FROM mkt2_pos_operations WHERE sourceDatabase=? AND DATE(operationDate)=?", [$database, $date]),
            'joyeriaTotal' => 0,
            'paymentsTotal' => (float)scalar($db, "SELECT COALESCE(SUM(total),0) FROM mkt2_pos_operations WHERE sourceDatabase=? AND DATE(operationDate)=?", [$database, $date]),
            'expensesTotal' => 0,
        ]);
    }
    $q = clean(query('query'));
    $like = "%$q%";
    if ($path === '/api/pos/operaciones') {
        $date = date_param(query('date') ?: date('Y-m-d'));
        $params = [$database, $date];
        $where = "sourceDatabase=? AND DATE(operationDate)=?";
        if ($q !== '') { $where .= " AND (folio LIKE ? OR sellerName LIKE ? OR hotel LIKE ?)"; array_push($params, $like, $like, $like); }
        json_response(rows($db, "SELECT source,folio,operationDate,sellerName,hotel,passengerCount,total,balance,paymentSummary FROM mkt2_pos_operations WHERE $where ORDER BY operationDate DESC LIMIT 200", $params));
    }
    if ($path === '/api/pos/comisiones') {
        $params = [$database]; $where = "sourceDatabase=?";
        if ($q !== '') { $where .= " AND (folio LIKE ? OR beneficiaryName LIKE ? OR sellerName LIKE ?)"; array_push($params, $like, $like, $like); }
        json_response(rows($db, "SELECT folio,saleDate,beneficiaryName,sellerName,baseAmount,commissionAmount,paid,paymentDate FROM mkt2_pos_commissions WHERE $where ORDER BY saleDate DESC LIMIT 200", $params));
    }
    if ($path === '/api/pos/vendedores' || $path === '/api/taxis/vendedores') {
        /*
         * POS conserva el filtro por base solicitado mediante ?database=.
         * Taxis consulta todos los vendedores disponibles porque normalmente
         * llama esta ruta sin enviar el parámetro database. El filtro anterior
         * utilizaba Mkt2 por defecto y por eso respondía [] aunque existieran
         * vendedores guardados bajo CompuadmoPlaza o JoyeriaPlaza.
         */
        if ($path === '/api/taxis/vendedores') {
            ensure_plaza_vendor_catalog_schema($db);
            $params = [];
            $where = "active=1 AND TRIM(COALESCE(vendorName, ''))<>''";

            if ($q !== '') {
                $where .= " AND (vendorKey LIKE ? OR vendorName LIKE ? OR phoneNumber LIKE ?)";
                array_push($params, $like, $like, $like);
            }

            $catalogItems = rows(
                $db,
                "SELECT
                    vendorKey,
                    vendorName AS name,
                    phoneNumber,
                    commissionPercent
                 FROM mkt2_catalogo_vendedoresPlaza
                 WHERE $where
                 ORDER BY vendorName
                 LIMIT 200",
                $params
            );

            $catalogHasRows = (int)scalar(
                $db,
                "SELECT COUNT(*)
                 FROM mkt2_catalogo_vendedoresPlaza
                 WHERE active=1
                   AND TRIM(COALESCE(vendorName, ''))<>''"
            ) > 0;

            if ($catalogHasRows) {
                json_response($catalogItems);
            }

            json_response(plaza_vendor_fallback_rows($db, $q, $like));
        }

        $params = [$database];
        $where = "sourceDatabase=?";

        if ($q !== '') {
            $where .= " AND (vendorKey LIKE ? OR name LIKE ? OR phoneNumber LIKE ?)";
            array_push($params, $like, $like, $like);
        }

        json_response(rows(
            $db,
            "SELECT vendorKey,name,phoneNumber,commissionPercent
             FROM mkt2_pos_vendors
             WHERE $where
             ORDER BY name
             LIMIT 200",
            $params
        ));
    }
    if ($path === '/api/pos/productos') {
        $params = [$database]; $where = "sourceDatabase=?";
        if ($q !== '') { $where .= " AND (code LIKE ? OR name LIKE ? OR CAST(productId AS CHAR) LIKE ?)"; array_push($params, $like, $like, $like); }
        json_response(rows($db, "SELECT productId,code,name,department,price,iva,currency,sourceDatabase FROM mkt2_pos_products WHERE $where ORDER BY name LIMIT 160", $params));
    }
    if ($path === '/api/pos/transportes') {
        json_response(rows($db, "SELECT code,name FROM mkt2_pos_transports WHERE sourceDatabase=? ORDER BY name", [$database]));
    }
    if ($path === '/api/pos/guias') {
        json_response(rows($db, "SELECT guideKey,name,company,phoneNumber FROM mkt2_pos_guides WHERE sourceDatabase=? ORDER BY name", [$database]));
    }
        // Resumen de camiones (AUTOCAR / MAYA CARIBE / TURICUN) para el cuadre del escritorio.
    // Sale de la base de la app de camiones, que es donde se escribe al momento; la copia en
    // SQL Server llega tarde y el corte del dia salia con llegadas incompletas.
    // Mismas formulas que el cuadre: ENTRARON = PAX - SALIERON, y DEJADA = suma de comision.
    if ($path === '/api/pos/camiones') {
        $camiones = camiones_db();
        $desde = clean(query('desde') ?? '');
        $hasta = clean(query('hasta') ?? '');
        if ($desde === '') $desde = date('Y-m-d');
        if ($hasta === '') $hasta = $desde;

        $filasCamiones = rows($camiones, "
            SELECT UPPER(TRIM(ch.Empresa))                                          AS empresa,
                   COALESCE(SUM(r.Pax_valido), 0)                                   AS pax,
                   GREATEST(
                       COALESCE(SUM(r.Pax_valido), 0)
                         - COALESCE(SUM(CASE WHEN r.Pax_valido > 0 THEN 1 ELSE 0 END), 0),
                       0)                                                           AS entraron,
                   COALESCE(SUM(CASE WHEN r.Pax_valido > 0 THEN 1 ELSE 0 END), 0)   AS salieron,
                   COUNT(DISTINCT r.Camion)                                         AS unidades,
                   COALESCE(SUM(r.Comision), 0)                                     AS dejada
            FROM registros r
            INNER JOIN choferes ch ON r.Id_chofer = ch.Id_chofer
            WHERE r.Fecha >= ? AND r.Fecha <= ?
              AND UPPER(TRIM(COALESCE(ch.Empresa, ''))) IN ('AUTOCAR', 'MAYA CARIBE', 'TURICUN')
            GROUP BY UPPER(TRIM(ch.Empresa))
            ORDER BY pax DESC", [$desde, $hasta]);
        // PDO regresa SUM()/COUNT() de MySQL como texto ("pax":"264"), no como numero JSON.
        // El escritorio (C#) espera numero real y tronaba en silencio al deserializar, asi que
        // el cuadre de camiones caia siempre al SQL Server viejo (vacio desde junio 2026) y
        // salia en $0 aunque esta respuesta ya tuviera el dato correcto (confirmado 2026-08-29).
        json_response(array_map(static fn(array $fila): array => [
            'empresa' => $fila['empresa'],
            'pax' => (int)$fila['pax'],
            'entraron' => (int)$fila['entraron'],
            'salieron' => (int)$fila['salieron'],
            'unidades' => (int)$fila['unidades'],
            'dejada' => (float)$fila['dejada'],
        ], $filasCamiones));
    }

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

    json_response(['error' => 'Ruta no encontrada', 'path' => $path], 404);
}

/// Conexion a la base de la app de camiones. Vive en OTRA cuenta de Hostinger, asi que se
/// conecta por host remoto y necesita que ese host este permitido en "MySQL remoto" de esa
/// cuenta. Las credenciales van en config.php, bloque 'mysql_camiones'.
function camiones_db(): PDO
{
    static $conexion = null;
    if ($conexion !== null) {
        return $conexion;
    }

    $config = require __DIR__ . '/config.php';
    if (!isset($config['mysql_camiones'])) {
        json_response(['error' => 'Falta la seccion mysql_camiones en config.php'], 500);
    }

    $m = $config['mysql_camiones'];
    $puerto = isset($m['port']) ? (int)$m['port'] : 3306;
    $charset = $m['charset'] ?? 'utf8mb4';
    $dsn = "mysql:host={$m['host']};port={$puerto};dbname={$m['database']};charset={$charset}";
    try {
        $conexion = new PDO($dsn, $m['user'], $m['password'], [
            PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION,
            PDO::ATTR_DEFAULT_FETCH_MODE => PDO::FETCH_ASSOC,
            PDO::ATTR_TIMEOUT => 10,
        ]);
    } catch (PDOException $e) {
        json_response([
            'error' => 'No pude conectar a la base de camiones',
            'detalle' => $e->getMessage(),
        ], 502);
    }
    return $conexion;
}

function mysql_db(array $config): PDO
{
    $m = $config['mysql'];
    $dsn = "mysql:host={$m['host']};dbname={$m['database']};charset={$m['charset']}";
    return new PDO($dsn, $m['user'], $m['password'], [
        PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION,
        PDO::ATTR_DEFAULT_FETCH_MODE => PDO::FETCH_ASSOC,
    ]);
}

function rows(PDO $db, string $sql, array $params = []): array
{
    $stmt = $db->prepare($sql);
    $stmt->execute($params);
    return array_map('normalize_row', $stmt->fetchAll());
}

function row(PDO $db, string $sql, array $params = []): ?array
{
    $items = rows($db, $sql, $params);
    return $items[0] ?? null;
}

function scalar(PDO $db, string $sql, array $params = []): mixed
{
    $stmt = $db->prepare($sql);
    $stmt->execute($params);
    return $stmt->fetchColumn();
}

function table_exists(PDO $db, string $table): bool
{
    return (int)scalar($db, "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=?", [$table]) > 0;
}

function ensure_plaza_vendor_catalog_schema(PDO $db): void
{
    static $done = false;
    if ($done) return;

    exec_sql($db, "CREATE TABLE IF NOT EXISTS mkt2_catalogo_vendedoresPlaza (
        vendorId INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
        vendorKey VARCHAR(220) NOT NULL,
        vendorName VARCHAR(220) NOT NULL,
        vendorCode VARCHAR(80) NOT NULL DEFAULT '',
        sourceDatabase VARCHAR(40) NOT NULL DEFAULT 'Mkt2',
        phoneNumber VARCHAR(80) NOT NULL DEFAULT '',
        commissionPercent DECIMAL(9,4) NOT NULL DEFAULT 0,
        active TINYINT(1) NOT NULL DEFAULT 1,
        createdAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
        updatedAt TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
        UNIQUE KEY UX_mkt2_catalogo_vendedoresPlaza_vendorKey (vendorKey),
        INDEX IX_mkt2_catalogo_vendedoresPlaza_active_name (active, vendorName)
    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

    ensure_column($db, 'mkt2_catalogo_vendedoresPlaza', 'vendorCode', "ALTER TABLE mkt2_catalogo_vendedoresPlaza ADD COLUMN vendorCode VARCHAR(80) NOT NULL DEFAULT ''");
    ensure_column($db, 'mkt2_catalogo_vendedoresPlaza', 'sourceDatabase', "ALTER TABLE mkt2_catalogo_vendedoresPlaza ADD COLUMN sourceDatabase VARCHAR(40) NOT NULL DEFAULT 'Mkt2'");
    ensure_column($db, 'mkt2_catalogo_vendedoresPlaza', 'phoneNumber', "ALTER TABLE mkt2_catalogo_vendedoresPlaza ADD COLUMN phoneNumber VARCHAR(80) NOT NULL DEFAULT ''");
    ensure_column($db, 'mkt2_catalogo_vendedoresPlaza', 'commissionPercent', "ALTER TABLE mkt2_catalogo_vendedoresPlaza ADD COLUMN commissionPercent DECIMAL(9,4) NOT NULL DEFAULT 0");
    ensure_column($db, 'mkt2_catalogo_vendedoresPlaza', 'active', "ALTER TABLE mkt2_catalogo_vendedoresPlaza ADD COLUMN active TINYINT(1) NOT NULL DEFAULT 1");
    ensure_column($db, 'mkt2_catalogo_vendedoresPlaza', 'createdAt', "ALTER TABLE mkt2_catalogo_vendedoresPlaza ADD COLUMN createdAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP");
    ensure_column($db, 'mkt2_catalogo_vendedoresPlaza', 'updatedAt', "ALTER TABLE mkt2_catalogo_vendedoresPlaza ADD COLUMN updatedAt TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP");

    $done = true;
}

function plaza_vendor_key(string $name): string
{
    $value = preg_replace('/\s+/', ' ', trim($name));
    if ($value === '') return '';
    return function_exists('mb_strtoupper')
        ? mb_strtoupper($value, 'UTF-8')
        : strtoupper($value);
}

function plaza_vendor_fallback_rows(PDO $db, string $q, string $like): array
{
    $params = [];
    $where = "TRIM(COALESCE(name, ''))<>''";

    if ($q !== '') {
        $where .= " AND (vendorKey LIKE ? OR name LIKE ? OR phoneNumber LIKE ?)";
        array_push($params, $like, $like, $like);
    }

    return rows(
        $db,
        "SELECT
            MIN(vendorKey) AS vendorKey,
            name,
            MAX(phoneNumber) AS phoneNumber,
            MAX(commissionPercent) AS commissionPercent
         FROM mkt2_pos_vendors
         WHERE $where
         GROUP BY name
         ORDER BY name
         LIMIT 200",
        $params
    );
}

function column(PDO $db, string $sql, array $params = []): array
{
    $stmt = $db->prepare($sql);
    $stmt->execute($params);
    return array_values(array_map('strval', $stmt->fetchAll(PDO::FETCH_COLUMN)));
}

function exec_sql(PDO $db, string $sql, array $params = []): int
{
    $stmt = $db->prepare($sql);
    $stmt->execute($params);
    return $stmt->rowCount();
}

function upsert(PDO $db, string $table, array $keys, array $data): void
{
    $columns = array_keys($data);
    $quoted = array_map(fn($c) => "`$c`", $columns);
    $placeholders = array_fill(0, count($columns), '?');
    $updates = array_values(array_filter($columns, fn($c) => !in_array($c, $keys, true)));
    $updateSql = implode(', ', array_map(fn($c) => "`$c`=VALUES(`$c`)", $updates));
    $sql = "INSERT INTO `$table` (" . implode(',', $quoted) . ") VALUES (" . implode(',', $placeholders) . ") ON DUPLICATE KEY UPDATE $updateSql";
    exec_sql($db, $sql, array_values($data));
}

function normalize_row(array $row): array
{
    foreach ($row as $key => $value) {
        if (is_string($value)) {
            $row[$key] = trim($value);
        }
        if (in_array($key, ['active', 'paid'], true)) {
            $row[$key] = (bool)$value;
        }
        if ($value !== null && str_ends_with(strtolower($key), 'date')) {
            $row[$key] = date('c', strtotime((string)$value));
        }
        if ($value !== null && in_array($key, ['createdAt'], true)) {
            $row[$key] = date('c', strtotime((string)$value));
        }
    }
    return $row;
}

function split_badges(mixed $value): array
{
    if (is_array($value)) {
        return normalize_badges_input($value, '');
    }

    $raw = clean($value);
    if ($raw === '') {
        return [];
    }

    $items = preg_split('/[,;\/|]+/', $raw) ?: [];
    $badges = [];
    foreach ($items as $item) {
        $badge = trim((string)$item);
        if ($badge !== '' && !in_array(strtoupper($badge), array_map('strtoupper', $badges), true)) {
            $badges[] = $badge;
        }
    }
    return $badges;
}

function normalize_badges_input(mixed $badgeIds, mixed $badgeId): array
{
    $values = [];
    if (is_array($badgeIds)) {
        foreach ($badgeIds as $item) {
            $values = array_merge($values, split_badges($item));
        }
    }
    $values = array_merge($values, split_badges($badgeId));

    $badges = [];
    $seen = [];
    foreach ($values as $value) {
        $badge = clean($value);
        $key = strtoupper($badge);
        if ($badge !== '' && !isset($seen[$key])) {
            $seen[$key] = true;
            $badges[] = $badge;
        }
    }
    return $badges;
}

/**
 * Normaliza el arreglo sellerBadges que manda la app: un elemento por gafete
 * entregado, con el vendedor que lo atendio. Si la app es vieja y no lo manda,
 * arma los pares con el vendedor unico del registro para no perder el dato.
 */
function normalize_seller_badges(mixed $sellerBadges, array $badgeIds, string $fallbackKey = '', string $fallbackName = ''): array
{
    $pairs = [];
    $seen = [];
    if (is_array($sellerBadges)) {
        foreach ($sellerBadges as $item) {
            if (!is_array($item)) {
                continue;
            }
            $badge = clean($item['badgeId'] ?? '');
            if ($badge === '') {
                continue;
            }
            $key = strtoupper($badge);
            if (isset($seen[$key])) {
                continue;
            }
            $seen[$key] = true;
            $pairs[] = [
                'badgeId' => $badge,
                'sellerKey' => clean($item['sellerKey'] ?? ''),
                'sellerName' => clean($item['sellerName'] ?? ''),
            ];
        }
    }

    // Gafetes que llegaron en badgeIds pero no traen vendedor propio.
    foreach ($badgeIds as $badge) {
        $key = strtoupper($badge);
        if (isset($seen[$key])) {
            continue;
        }
        $seen[$key] = true;
        $pairs[] = [
            'badgeId' => $badge,
            'sellerKey' => $fallbackKey,
            'sellerName' => $fallbackName,
        ];
    }

    return $pairs;
}

/** Vendedor que queda en el registro: el primero capturado con nombre o clave. */
function primary_seller_from_badges(array $pairs): array
{
    foreach ($pairs as $pair) {
        if (($pair['sellerName'] ?? '') !== '' || ($pair['sellerKey'] ?? '') !== '') {
            return ['sellerKey' => $pair['sellerKey'] ?? '', 'sellerName' => $pair['sellerName'] ?? ''];
        }
    }
    return ['sellerKey' => '', 'sellerName' => ''];
}

/** Reemplaza el detalle de vendedores de un registro. */
function save_trip_record_sellers(PDO $db, string $recordId, array $pairs, string $branchCode = ''): void
{
    if ($recordId === '') {
        return;
    }
    ensure_sync_schema($db);
    exec_sql($db, "DELETE FROM mkt2_trip_record_sellers WHERE recordId=?", [$recordId]);
    foreach ($pairs as $pair) {
        $badge = clean($pair['badgeId'] ?? '');
        if ($badge === '') {
            continue;
        }
        upsert($db, 'mkt2_trip_record_sellers', ['recordId', 'badgeId'], [
            'recordId' => $recordId,
            'badgeId' => $badge,
            'sellerKey' => clean($pair['sellerKey'] ?? ''),
            'sellerName' => clean($pair['sellerName'] ?? ''),
            'branchCode' => $branchCode,
            'createdAt' => date('Y-m-d H:i:s'),
            'syncStatus' => 'pending',
            'source' => 'hostinger',
        ]);
    }
}

/** Detalle de vendedores por registro, listo para responderle a la app. */
function trip_record_sellers_map(PDO $db, array $recordIds): array
{
    $ids = [];
    foreach ($recordIds as $id) {
        $value = clean((string)$id);
        if ($value !== '') {
            $ids[$value] = true;
        }
    }
    if (!$ids) {
        return [];
    }
    ensure_sync_schema($db);
    $ids = array_keys($ids);
    $placeholders = implode(',', array_fill(0, count($ids), '?'));
    $rows = rows($db, "SELECT recordId,badgeId,sellerKey,sellerName FROM mkt2_trip_record_sellers WHERE recordId IN ($placeholders) ORDER BY recordId, badgeId", $ids);
    $map = [];
    foreach ($rows as $row) {
        $map[(string)$row['recordId']][] = [
            'badgeId' => (string)$row['badgeId'],
            'sellerKey' => (string)$row['sellerKey'],
            'sellerName' => (string)$row['sellerName'],
        ];
    }
    return $map;
}

/** Pega sellerBadges a cada fila de registros que se devuelve. */
function attach_trip_record_sellers(PDO $db, array $rows): array
{
    if (!$rows) {
        return $rows;
    }
    $map = trip_record_sellers_map($db, array_column($rows, 'recordId'));
    foreach ($rows as $index => $row) {
        $rows[$index]['sellerBadges'] = $map[(string)($row['recordId'] ?? '')] ?? [];
    }
    return $rows;
}

/**
 * Guarda cada gafete con SU vendedor. Antes todos los gafetes de una llegada
 * se quedaban con el mismo vendedor porque solo llegaba uno.
 */
function sync_taxista_badges_by_seller(PDO $db, ?int $taxistaId, string $taxistaName, array $pairs, bool $active, string $branchCode = ''): void
{
    foreach ($pairs as $pair) {
        $badge = clean($pair['badgeId'] ?? '');
        if ($badge === '') {
            continue;
        }
        sync_taxista_badges($db, $taxistaId, $taxistaName, $badge, $active, $branchCode, clean($pair['sellerKey'] ?? ''), clean($pair['sellerName'] ?? ''));
    }
}

function sync_taxista_badges(PDO $db, ?int $taxistaId, string $taxistaName, mixed $badgeValue, bool $active, string $branchCode = '', string $vendorKey = '', string $vendorName = ''): void
{
    $badges = split_badges($badgeValue);
    if (!$badges) {
        return;
    }

    ensure_sync_schema($db);
    foreach ($badges as $badge) {
        upsert($db, 'mkt2_gafetes', ['badgeId', 'cycle'], [
            'badgeId' => $badge,
            'barcode' => $badge,
            'status' => $active ? 'Asignado' : 'Disponible',
            'cycle' => 1,
            'taxistaId' => $taxistaId,
            'taxistaName' => $taxistaName,
            'vendorKey' => $vendorKey,
            'vendorName' => $vendorName,
            'createdAt' => date('Y-m-d H:i:s'),
            'syncStatus' => 'pending',
            'source' => 'hostinger',
            'branchCode' => $branchCode,
        ]);
    }
}

function find_catalog_by_linked_gafete(PDO $db, string $badge, string $branchCode = ''): ?array
{
    $item = row($db, "SELECT
            c.catalogId,
            COALESCE(NULLIF(GROUP_CONCAT(DISTINCT g.badgeId ORDER BY CAST(g.badgeId AS UNSIGNED), g.badgeId SEPARATOR ', '), ''), c.badgeId) AS badgeId,
            c.driverName,
            c.phoneNumber,
            c.plate,
            c.vehicleModel,
            c.unitNumber,
            c.serviceType,
            c.site,
            c.hotel,
            CASE WHEN LOWER(c.notes) LIKE '%importado desde dejadas%' OR LOWER(c.notes) LIKE '%origen%' OR LOWER(c.notes) LIKE '%mkt%' THEN '' ELSE c.notes END AS notes,
            c.suggestedAmount
        FROM mkt2_gafetes exact
        INNER JOIN mkt2_catalog_taxis c ON c.catalogId=exact.taxistaId
        LEFT JOIN (
            SELECT taxistaId, badgeId
            FROM mkt2_gafetes
            WHERE taxistaId IS NOT NULL" . ($branchCode !== '' ? " AND branchCode=?" : "") . "
        ) g ON g.taxistaId=c.catalogId
        WHERE c.active=1" . ($branchCode !== '' ? " AND c.branchCode=?" : "") . ($branchCode !== '' ? " AND exact.branchCode=?" : "") . " AND exact.badgeId=?
        GROUP BY c.catalogId,c.badgeId,c.driverName,c.phoneNumber,c.plate,c.vehicleModel,c.unitNumber,c.serviceType,c.site,c.hotel,c.notes,c.suggestedAmount
        LIMIT 1", $branchCode !== '' ? [$branchCode, $branchCode, $branchCode, $badge] : [$badge]);
    if ($item) {
        return $item;
    }

    if (!table_exists($db, 'mkt2_dejadas_taxistas_recientes')) {
        return null;
    }

    return row($db, "SELECT
            900000000 + r.recentId AS catalogId,
            COALESCE(NULLIF(GROUP_CONCAT(DISTINCT g.badgeId ORDER BY CAST(g.badgeId AS UNSIGNED), g.badgeId SEPARATOR ', '), ''), r.lastFolio) AS badgeId,
            r.driverName,
            r.phoneNumber,
            CASE WHEN r.unitNumber REGEXP '[[:alpha:]]' AND r.unitNumber LIKE '%-%' THEN r.unitNumber ELSE '' END AS plate,
            '' AS vehicleModel,
            r.unitNumber,
            r.transportType AS serviceType,
            r.site,
            r.origin AS hotel,
            '' AS notes,
            r.suggestedAmount
        FROM mkt2_gafetes exact
        INNER JOIN mkt2_dejadas_taxistas_recientes r ON 900000000 + r.recentId=exact.taxistaId
        LEFT JOIN mkt2_gafetes g ON g.taxistaId=900000000 + r.recentId
        WHERE r.active=1 AND exact.badgeId=?
        GROUP BY r.recentId,r.lastFolio,r.driverName,r.phoneNumber,r.unitNumber,r.transportType,r.site,r.origin,r.suggestedAmount
        LIMIT 1", [$badge]);
}

function query(string $key): ?string { return isset($_GET[$key]) ? (string)$_GET[$key] : null; }
function request_raw_body(): string
{
    static $rawBody = null;
    if ($rawBody === null) {
        $rawBody = file_get_contents('php://input') ?: '';
    }
    return $rawBody;
}

function body(): array
{
    $raw = request_raw_body();
    $contentType = strtolower($_SERVER['CONTENT_TYPE'] ?? '');

    if (str_contains($contentType, 'application/json')) {
        $data = json_decode($raw, true);
        if (!is_array($data)) {
            json_response([
                'error' => 'JSON inválido',
                'contentType' => $_SERVER['CONTENT_TYPE'] ?? '',
                'contentLength' => $_SERVER['CONTENT_LENGTH'] ?? '',
                'rawLength' => strlen($raw),
                'rawBase64' => base64_encode($raw),
                'rawHexPrefix' => bin2hex(substr($raw, 0, 80)),
                'jsonError' => json_last_error_msg(),
            ], 400);
        }
        return $data;
    }

    if ($raw !== '') {
        $data = [];
        parse_str($raw, $data);
        if (is_array($data) && $data !== []) {
            return $data;
        }
    }

    return is_array($_POST ?? null) ? $_POST : [];
}
function clean(mixed $value): string { return trim((string)($value ?? '')); }
function non_negative_int(mixed $value): int { return max(0, (int)$value); }
function date_param(?string $value): string { return date('Y-m-d', strtotime(clean($value) ?: 'today')); }
function mysql_datetime(mixed $value): string { return date('Y-m-d H:i:s', strtotime(clean($value) ?: 'now')); }
function next_trip_record_id(PDO $db): string
{
    exec_sql($db, "CREATE TABLE IF NOT EXISTS mkt2_trip_counters (
        counterName VARCHAR(40) NOT NULL PRIMARY KEY,
        lastNumber INT NOT NULL DEFAULT 0
    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
    $last = (int)scalar($db, "SELECT COALESCE(MAX(
        CASE
            WHEN recordId REGEXP '^[0-9]+$' THEN CAST(recordId AS UNSIGNED)
            WHEN recordId REGEXP '^AP[0-9]{1,4}$' THEN CAST(SUBSTRING(recordId, 3) AS UNSIGNED)
            WHEN recordId REGEXP '^TX-[0-9]+$' THEN CAST(SUBSTRING(recordId, 4) AS UNSIGNED)
            ELSE 0
        END
    ), 0) FROM mkt2_trip_records");
    exec_sql($db, "INSERT INTO mkt2_trip_counters (counterName,lastNumber)
        VALUES ('trip_record', ?)
        ON DUPLICATE KEY UPDATE lastNumber=GREATEST(lastNumber, VALUES(lastNumber))", [$last]);
    exec_sql($db, "UPDATE mkt2_trip_counters SET lastNumber=LAST_INSERT_ID(lastNumber + 1) WHERE counterName='trip_record'");
    $next = (int)scalar($db, "SELECT LAST_INSERT_ID()");
    return str_pad((string)$next, 4, '0', STR_PAD_LEFT);
}
function parse_bool(mixed $value): bool { return $value === true || $value === 1 || strtolower((string)$value) === 'true' || (string)$value === '1'; }
function valid_text_sql(string $column): string
{
    return "TRIM($column)<>'' AND CHAR_LENGTH(TRIM($column))>1 AND TRIM($column) REGEXP '[[:alpha:]]' AND TRIM($column) NOT REGEXP '^[0-9]+$' AND TRIM($column) NOT REGEXP '^[[:punct:]]+$'";
}
function pos_database(?string $value): string
{
    $v = strtolower(clean($value));
    if (in_array($v, ['compuadmoplaza', 'compuadmo'], true)) return 'CompuadmoPlaza';
    if (in_array($v, ['joyeriaplaza', 'joyeria'], true)) return 'JoyeriaPlaza';
    return 'Mkt2';
}
function json_response(mixed $data, int $status = 200): never
{
    http_response_code($status);
    echo json_encode($data, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
    exit;
}

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
