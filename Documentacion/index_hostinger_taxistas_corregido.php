<?php

declare(strict_types=1);

date_default_timezone_set('America/Mexico_City');

$config = require __DIR__ . '/config.php';
require_once __DIR__ . '/src/GafeteValidator.php';
require_once __DIR__ . '/src/GafeteRepository.php';
require_once __DIR__ . '/src/GafeteController.php';

header('Content-Type: application/json; charset=utf-8');
header('Access-Control-Allow-Origin: *');
header('Access-Control-Allow-Headers: Content-Type, Authorization, X-Branch-Code, X-Sync-Token');
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
    $path = request_path();
    $db = mysql_db($config);

    ensure_schema($db);

    if ($path === '/') {
        json_response([
            'mensaje' => 'API HOKA Taxis Casco Viejo activa',
            'branchCode' => $config['branch']['branchCode'],
            'fecha' => gmdate('c'),
        ]);
    }

    if ($path === '/health') {
        json_response([
            'ok' => true,
            'mysql' => (int)scalar($db, 'SELECT 1') === 1,
            'branchCode' => $config['branch']['branchCode'],
            'fecha' => gmdate('c'),
        ]);
    }

    if (str_starts_with($path, '/api/auth/')) {
        route_auth($db, $config, $method, $path);
    }

    if ($method === 'GET' && $path === '/api/taxis/dashboard') {
        json_response([
            'todayRecords' => (int)scalar($db, "SELECT COUNT(*) FROM mkt2_trip_records_Casco WHERE recordDate>=CURDATE() AND recordDate<DATE_ADD(CURDATE(), INTERVAL 1 DAY)"),
            'activeDrivers' => (int)scalar($db, "SELECT COUNT(*) FROM mkt2_catalog_taxis_Casco WHERE active=1"),
            'hotelsCount' => (int)scalar($db, "SELECT COUNT(DISTINCT hotel) FROM mkt2_catalog_taxis_Casco WHERE active=1 AND " . valid_text_sql('hotel')),
            'serviceTypesCount' => (int)scalar($db, "SELECT COUNT(DISTINCT serviceType) FROM mkt2_catalog_taxis_Casco WHERE active=1 AND " . valid_text_sql('serviceType')),
        ]);
    }

    if ($method === 'GET' && $path === '/api/taxis/options') {
        json_response([
            'hotels' => column($db, "SELECT DISTINCT hotel AS item FROM mkt2_catalog_taxis_Casco WHERE active=1 AND " . valid_text_sql('hotel') . " ORDER BY hotel"),
            'serviceTypes' => column($db, "SELECT DISTINCT serviceType AS item FROM mkt2_catalog_taxis_Casco WHERE active=1 AND " . valid_text_sql('serviceType') . " ORDER BY serviceType"),
            'sites' => column($db, "SELECT DISTINCT site AS item FROM mkt2_catalog_taxis_Casco WHERE active=1 AND " . valid_text_sql('site') . " ORDER BY site"),
        ]);
    }

    if ($method === 'GET' && $path === '/api/taxis/catalogo') {
        $q = clean(query('query'));
        if ($q === '') {
            json_response(null, 404);
        }
        $badgeItem = find_catalog_by_linked_gafete($db, $q);
        if ($badgeItem) {
            json_response($badgeItem);
        }
        $like = "%$q%";
        $item = row($db, "SELECT catalogId,badgeId,driverName,phoneNumber,plate,vehicleModel,unitNumber,serviceType,site,hotel,notes,suggestedAmount
            FROM mkt2_catalog_taxis_Casco
            WHERE active=1 AND (badgeId=? OR driverName LIKE ? OR plate LIKE ? OR unitNumber LIKE ?)
            ORDER BY CASE WHEN badgeId=? THEN 0 ELSE 1 END, driverName
            LIMIT 1", [$q, $like, $like, $like, $q]);
        $item ? json_response($item) : json_response(null, 404);
    }

    if ($method === 'GET' && $path === '/api/taxis/catalogo/buscar') {
        $q = clean(query('query'));
        if ($q === '') {
            json_response([]);
        }
        $badgeItem = find_catalog_by_linked_gafete($db, $q);
        if ($badgeItem) {
            json_response([$badgeItem]);
        }
        $like = "%$q%";
        json_response(rows($db, "SELECT catalogId,badgeId,driverName,phoneNumber,plate,vehicleModel,unitNumber,serviceType,site,hotel,notes,suggestedAmount
            FROM mkt2_catalog_taxis_Casco
            WHERE active=1 AND (driverName LIKE ? OR badgeId LIKE ? OR plate LIKE ? OR unitNumber LIKE ?)
            ORDER BY driverName
            LIMIT 8", [$like, $like, $like, $like]));
    }

    if ($method === 'GET' && $path === '/api/taxis/tarifas') {
        if (!table_exists($db, 'mkt2_service_rate_rules_Casco')) {
            json_response([]);
        }
        json_response(rows($db, "SELECT serviceType,description,amount AS baseAmount, CAST(0 AS DECIMAL(18,2)) AS extraPersonAmount, origin, minPassengers, maxPassengers
            FROM mkt2_service_rate_rules_Casco
            WHERE active=1
            ORDER BY serviceType, origin, minPassengers"));
    }

    if ($method === 'GET' && $path === '/api/taxis/taxistas') {
        $page = max(1, (int)(query('page') ?? 1));
        $pageSize = min(100, max(1, (int)(query('pageSize') ?? 100)));
        $offset = ($page - 1) * $pageSize;
        $q = clean(query('query'));
        $active = clean(query('active'));

        $where = '1=1';
        $params = [];
        if ($q !== '') {
            $where .= " AND (CAST(catalogId AS CHAR) LIKE ? OR driverName LIKE ? OR phoneNumber LIKE ? OR serviceType LIKE ? OR plate LIKE ? OR unitNumber LIKE ? OR site LIKE ? OR hotel LIKE ?)";
            $like = "%$q%";
            array_push($params, $like, $like, $like, $like, $like, $like, $like, $like);
        }
        if ($active === '1' || $active === '0') {
            $where .= ' AND active=?';
            $params[] = (int)$active;
        }

        $total = (int)scalar($db, "SELECT COUNT(*) FROM mkt2_catalog_taxis_Casco WHERE $where", $params);
        $items = rows($db, "SELECT catalogId,badgeId,driverName,phoneNumber,plate,vehicleModel,unitNumber,serviceType,site,hotel,notes,active
            FROM mkt2_catalog_taxis_Casco
            WHERE $where
            ORDER BY active DESC, driverName
            LIMIT $pageSize OFFSET $offset", $params);
        json_response(['items' => $items, 'page' => $page, 'pageSize' => $pageSize, 'total' => $total]);
    }

    if ($method === 'POST' && $path === '/api/taxis/taxistas') {
        $data = body();
        $id = next_catalog_id($db);
        $badgeIds = normalize_badges_input($data['badgeIds'] ?? [], $data['badgeId'] ?? '');
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
        json_response(require_taxista($db, $id), 201);
    }

    if (preg_match('#^/api/taxis/taxistas/([0-9]+)$#', $path, $matches) === 1 && $method === 'PUT') {
        $id = (int)$matches[1];
        $current = require_taxista($db, $id);
        $data = body();
        $badgeIds = normalize_badges_input($data['badgeIds'] ?? [], $data['badgeId'] ?? ($current['badgeId'] ?? ''));
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
        json_response(require_taxista($db, $id));
    }

    if (preg_match('#^/api/taxis/taxistas/([0-9]+)$#', $path, $matches) === 1 && $method === 'DELETE') {
        $id = (int)$matches[1];
        require_taxista($db, $id);
        exec_sql($db, "UPDATE mkt2_catalog_taxis_Casco SET active=0, updatedAt=NOW() WHERE catalogId=?", [$id]);
        json_response(['ok' => true]);
    }

    if ($method === 'GET' && $path === '/api/taxis/registros') {
        $where = '1=1';
        $params = [];
        $date = clean(query('date'));
        $dateFrom = clean(query('dateFrom'));
        $dateTo = clean(query('dateTo'));
        $q = clean(query('query'));
        if ($date !== '') {
            $where .= ' AND DATE(recordDate)=?';
            $params[] = $date;
        } else {
            if ($dateFrom !== '') {
                $where .= ' AND DATE(recordDate)>=?';
                $params[] = $dateFrom;
            }
            if ($dateTo !== '') {
                $where .= ' AND DATE(recordDate)<=?';
                $params[] = $dateTo;
            }
        }
        if ($q !== '') {
            $where .= " AND (recordId LIKE ? OR badgeId LIKE ? OR driverName LIKE ? OR hotel LIKE ? OR serviceType LIKE ?)";
            $like = "%$q%";
            array_push($params, $like, $like, $like, $like, $like);
        }
        json_response(rows($db, "SELECT recordId,catalogId,badgeId,driverName,driverPhone,contactPhone,nationality,plate,vehicleModel,unitNumber,hotel,origin,site,destination,passengerCount,serviceType,tripCost,paymentMethod,notes,recordDate,assignedBranch,assignedBranchCode,assignedBranchName,payoutStatus,payoutDate,payoutUser,payoutTicket,createdAt,updatedAt
            FROM mkt2_trip_records_Casco
            WHERE $where
            ORDER BY recordDate DESC, recordId DESC
            LIMIT 300", $params));
    }

    if ($method === 'POST' && $path === '/api/taxis/registros') {
        $data = body();
        $recordId = next_trip_record_id($db);
        upsert($db, 'mkt2_trip_records_Casco', ['recordId'], [
            'recordId' => $recordId,
            'catalogId' => isset($data['catalogId']) && $data['catalogId'] !== '' ? (int)$data['catalogId'] : null,
            'badgeId' => implode(', ', normalize_badges_input($data['badgeIds'] ?? [], $data['badgeId'] ?? '')),
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
            'passengerCount' => (int)($data['passengerCount'] ?? 0),
            'serviceType' => clean($data['serviceType'] ?? ''),
            'tripCost' => (float)($data['tripCost'] ?? 0),
            'paymentMethod' => clean($data['paymentMethod'] ?? 'Efectivo'),
            'notes' => clean($data['notes'] ?? ''),
            'recordDate' => mysql_datetime($data['recordDate'] ?? null),
            'assignedBranch' => clean($data['assignedBranch'] ?? $config['branch']['branchLabel']),
            'assignedBranchCode' => clean($data['assignedBranchCode'] ?? $config['branch']['branchCode']),
            'assignedBranchName' => clean($data['assignedBranchName'] ?? $config['branch']['branchName']),
            'payoutStatus' => 'pendiente',
            'createdAt' => date('Y-m-d H:i:s'),
            'syncStatus' => 'pending',
            'source' => 'hostinger',
        ]);
        json_response(['ok' => true, 'recordId' => $recordId], 201);
    }

    if (preg_match('#^/api/taxis/registros/([^/]+)/pagar$#', $path, $matches) === 1 && $method === 'POST') {
        $recordId = clean($matches[1]);
        $payload = body();
        exec_sql($db, "UPDATE mkt2_trip_records_Casco
            SET payoutStatus='pagado',
                payoutDate=NOW(),
                payoutUser=?,
                updatedAt=NOW(),
                syncStatus='pending',
                source='hostinger'
            WHERE recordId=?", [clean($payload['user'] ?? ''), $recordId]);
        $item = row($db, "SELECT recordId,catalogId,badgeId,driverName,driverPhone,contactPhone,nationality,plate,vehicleModel,unitNumber,hotel,origin,site,destination,passengerCount,serviceType,tripCost,paymentMethod,notes,recordDate,assignedBranch,assignedBranchCode,assignedBranchName,payoutStatus,payoutDate,payoutUser,payoutTicket,createdAt,updatedAt
            FROM mkt2_trip_records_Casco
            WHERE recordId=?", [$recordId]);
        $item ? json_response($item) : json_response(['error' => 'Registro no encontrado'], 404);
    }

    if ($method === 'GET' && $path === '/api/taxis/gafetes') {
        json_response(rows($db, "SELECT badgeId,barcode,status,cycle,taxistaId,taxistaName,createdAt
            FROM mkt2_gafetes_Casco
            ORDER BY createdAt DESC
            LIMIT 150"));
    }

    if ($method === 'POST' && $path === '/api/taxis/gafetes/sync') {
        $controller = new \CascoGafetes\GafeteController(
            new \CascoGafetes\GafeteValidator(),
            new \CascoGafetes\GafeteRepository($db),
            [
                'app' => [
                    'environment' => 'production',
                    'required_branch_code' => 'CV',
                    'reject_branch_codes' => ['28'],
                    'max_batch_size' => 500,
                    'require_token_in_production' => false,
                    'sync_token' => null,
                ],
            ]
        );

        $controller->sync();
    }

    if ($method === 'POST' && $path === '/api/taxis/gafetes/generar') {
        $data = body();
        $quantity = min(100, max(1, (int)($data['quantity'] ?? 1)));
        $generated = [];
        for ($i = 0; $i < $quantity; $i++) {
            $number = next_badge_number($db, parse_bool($data['resetCounter'] ?? false) && $i === 0);
            $badgeId = 'CV-' . str_pad((string)$number, 4, '0', STR_PAD_LEFT);
            upsert($db, 'mkt2_gafetes_Casco', ['badgeId', 'cycle'], [
                'badgeId' => $badgeId,
                'barcode' => $badgeId,
                'status' => empty($data['taxistaId']) ? 'Disponible' : 'Asignado',
                'cycle' => 1,
                'taxistaId' => empty($data['taxistaId']) ? null : (int)$data['taxistaId'],
                'taxistaName' => clean($data['taxistaName'] ?? ''),
                'createdAt' => date('Y-m-d H:i:s'),
                'syncStatus' => 'pending',
                'source' => 'hostinger',
            ]);
            $generated[] = [
                'badgeId' => $badgeId,
                'barcode' => $badgeId,
                'status' => empty($data['taxistaId']) ? 'Disponible' : 'Asignado',
                'cycle' => 1,
                'taxistaId' => empty($data['taxistaId']) ? null : (int)$data['taxistaId'],
                'taxistaName' => clean($data['taxistaName'] ?? ''),
                'createdAt' => date('c'),
            ];
        }
        json_response($generated, 201);
    }

    json_response(['error' => 'Ruta no encontrada', 'path' => $path], 404);
}

function route_auth(PDO $db, array $config, string $method, string $path): void
{
    if ($method === 'GET' && $path === '/api/auth/branches') {
        json_response([$config['branch']]);
    }

    if ($method === 'POST' && $path === '/api/auth/login') {
        $payload = body();
        $username = clean($payload['username'] ?? '');
        $password = (string)($payload['password'] ?? '');
        $user = row($db, "SELECT userId,username,passwordHash,fullName,role,branchCode,branchName,branchLabel,active,canViewHome,canViewRegister,canViewTaxistas,canViewHistory,canViewUsers,canViewAllBranches
            FROM mkt2_users_Casco
            WHERE username=?
            LIMIT 1", [$username]);
        if (!$user || !password_verify($password, (string)$user['passwordHash'])) {
            json_response(['error' => 'Credenciales invalidas'], 401);
        }
        if (!(bool)$user['active']) {
            json_response(['error' => 'Usuario inactivo'], 403);
        }
        unset($user['passwordHash']);
        json_response(['user' => $user]);
    }

    if ($method === 'GET' && $path === '/api/auth/users') {
        json_response(rows($db, "SELECT userId,username,fullName,role,branchCode,branchName,branchLabel,active,canViewHome,canViewRegister,canViewTaxistas,canViewHistory,canViewUsers,canViewAllBranches,createdAt,updatedAt
            FROM mkt2_users_Casco
            ORDER BY active DESC, username"));
    }

    if ($method === 'POST' && $path === '/api/auth/users') {
        $data = body();
        $password = (string)($data['password'] ?? '');
        if ($password === '') {
            json_response(['error' => 'Password requerido'], 422);
        }
        exec_sql($db, "INSERT INTO mkt2_users_Casco
            (username,passwordHash,fullName,role,branchCode,branchName,branchLabel,active,canViewHome,canViewRegister,canViewTaxistas,canViewHistory,canViewUsers,canViewAllBranches)
            VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?)", [
            clean($data['username'] ?? ''),
            password_hash($password, PASSWORD_DEFAULT),
            clean($data['fullName'] ?? ''),
            clean($data['role'] ?? 'operador'),
            'CV',
            'Casco Viejo',
            'CV - Casco Viejo',
            parse_bool($data['active'] ?? true) ? 1 : 0,
            parse_bool($data['canViewHome'] ?? true) ? 1 : 0,
            parse_bool($data['canViewRegister'] ?? true) ? 1 : 0,
            parse_bool($data['canViewTaxistas'] ?? true) ? 1 : 0,
            parse_bool($data['canViewHistory'] ?? true) ? 1 : 0,
            parse_bool($data['canViewUsers'] ?? false) ? 1 : 0,
            parse_bool($data['canViewAllBranches'] ?? false) ? 1 : 0,
        ]);
        json_response(row($db, "SELECT userId,username,fullName,role,branchCode,branchName,branchLabel,active,canViewHome,canViewRegister,canViewTaxistas,canViewHistory,canViewUsers,canViewAllBranches,createdAt,updatedAt
            FROM mkt2_users_Casco
            WHERE userId=LAST_INSERT_ID()"), 201);
    }

    if (preg_match('#^/api/auth/users/([0-9]+)$#', $path, $matches) === 1 && $method === 'PUT') {
        $id = (int)$matches[1];
        $current = row($db, "SELECT userId,username,fullName,role,branchCode,branchName,branchLabel,active,canViewHome,canViewRegister,canViewTaxistas,canViewHistory,canViewUsers,canViewAllBranches
            FROM mkt2_users_Casco
            WHERE userId=?", [$id]);
        if (!$current) {
            json_response(['error' => 'Usuario no encontrado'], 404);
        }
        $data = body();
        exec_sql($db, "UPDATE mkt2_users_Casco
            SET username=?,
                fullName=?,
                role=?,
                branchCode='CV',
                branchName='Casco Viejo',
                branchLabel='CV - Casco Viejo',
                active=?,
                canViewHome=?,
                canViewRegister=?,
                canViewTaxistas=?,
                canViewHistory=?,
                canViewUsers=?,
                canViewAllBranches=?,
                updatedAt=NOW()
            WHERE userId=?", [
            clean($data['username'] ?? $current['username']),
            clean($data['fullName'] ?? $current['fullName']),
            clean($data['role'] ?? $current['role']),
            array_key_exists('active', $data) ? (parse_bool($data['active']) ? 1 : 0) : ((bool)$current['active'] ? 1 : 0),
            array_key_exists('canViewHome', $data) ? (parse_bool($data['canViewHome']) ? 1 : 0) : ((bool)$current['canViewHome'] ? 1 : 0),
            array_key_exists('canViewRegister', $data) ? (parse_bool($data['canViewRegister']) ? 1 : 0) : ((bool)$current['canViewRegister'] ? 1 : 0),
            array_key_exists('canViewTaxistas', $data) ? (parse_bool($data['canViewTaxistas']) ? 1 : 0) : ((bool)$current['canViewTaxistas'] ? 1 : 0),
            array_key_exists('canViewHistory', $data) ? (parse_bool($data['canViewHistory']) ? 1 : 0) : ((bool)$current['canViewHistory'] ? 1 : 0),
            array_key_exists('canViewUsers', $data) ? (parse_bool($data['canViewUsers']) ? 1 : 0) : ((bool)$current['canViewUsers'] ? 1 : 0),
            array_key_exists('canViewAllBranches', $data) ? (parse_bool($data['canViewAllBranches']) ? 1 : 0) : ((bool)$current['canViewAllBranches'] ? 1 : 0),
            $id,
        ]);
        json_response(row($db, "SELECT userId,username,fullName,role,branchCode,branchName,branchLabel,active,canViewHome,canViewRegister,canViewTaxistas,canViewHistory,canViewUsers,canViewAllBranches,createdAt,updatedAt
            FROM mkt2_users_Casco
            WHERE userId=?", [$id]));
    }

    if (preg_match('#^/api/auth/users/([0-9]+)$#', $path, $matches) === 1 && $method === 'DELETE') {
        exec_sql($db, "UPDATE mkt2_users_Casco SET active=0, updatedAt=NOW() WHERE userId=?", [(int)$matches[1]]);
        json_response(['ok' => true]);
    }

    json_response(['error' => 'Ruta no encontrada', 'path' => $path], 404);
}

function ensure_schema(PDO $db): void
{
    exec_sql($db, "CREATE TABLE IF NOT EXISTS mkt2_users_Casco (
        userId INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
        username VARCHAR(80) NOT NULL,
        passwordHash VARCHAR(255) NOT NULL,
        fullName VARCHAR(180) NOT NULL DEFAULT '',
        role VARCHAR(30) NOT NULL DEFAULT 'operador',
        branchCode VARCHAR(50) NOT NULL DEFAULT 'CV',
        branchName VARCHAR(180) NOT NULL DEFAULT 'Casco Viejo',
        branchLabel VARCHAR(220) NOT NULL DEFAULT 'CV - Casco Viejo',
        active TINYINT(1) NOT NULL DEFAULT 1,
        canViewHome TINYINT(1) NOT NULL DEFAULT 1,
        canViewRegister TINYINT(1) NOT NULL DEFAULT 1,
        canViewTaxistas TINYINT(1) NOT NULL DEFAULT 1,
        canViewHistory TINYINT(1) NOT NULL DEFAULT 1,
        canViewUsers TINYINT(1) NOT NULL DEFAULT 0,
        canViewAllBranches TINYINT(1) NOT NULL DEFAULT 0,
        createdAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
        updatedAt TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
        UNIQUE KEY ux_mkt2_users_casco_username (username)
    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci");
}

function require_taxista(PDO $db, int $id): array
{
    $item = row($db, "SELECT catalogId,badgeId,driverName,phoneNumber,plate,vehicleModel,unitNumber,serviceType,site,hotel,notes,suggestedAmount,active,createdAt
        FROM mkt2_catalog_taxis_Casco
        WHERE catalogId=?", [$id]);
    if (!$item) {
        json_response(['error' => 'Taxista no encontrado'], 404);
    }
    return $item;
}

function next_catalog_id(PDO $db): int
{
    return (int)scalar($db, "SELECT COALESCE(MAX(catalogId), 100000) + 1 FROM mkt2_catalog_taxis_Casco");
}

function next_trip_record_id(PDO $db): string
{
    $last = (int)scalar($db, "SELECT COALESCE(lastNumber, 0) FROM mkt2_trip_counters_Casco WHERE counterName='records'");
    if ($last === 0) {
        $seed = (int)scalar($db, "SELECT COALESCE(MAX(CAST(recordId AS UNSIGNED)), 0) FROM mkt2_trip_records_Casco WHERE recordId REGEXP '^[0-9]+$'");
        exec_sql($db, "INSERT INTO mkt2_trip_counters_Casco (counterName, lastNumber)
            VALUES ('records', ?)
            ON DUPLICATE KEY UPDATE lastNumber=GREATEST(lastNumber, VALUES(lastNumber))", [$seed]);
    }
    exec_sql($db, "UPDATE mkt2_trip_counters_Casco SET lastNumber=LAST_INSERT_ID(lastNumber + 1) WHERE counterName='records'");
    return str_pad((string)((int)scalar($db, "SELECT LAST_INSERT_ID()")), 4, '0', STR_PAD_LEFT);
}

function next_badge_number(PDO $db, bool $reset): int
{
    if ($reset) {
        exec_sql($db, "INSERT INTO mkt2_trip_counters_Casco (counterName, lastNumber)
            VALUES ('badges', 0)
            ON DUPLICATE KEY UPDATE lastNumber=0");
    } else {
        $seed = (int)scalar($db, "SELECT COALESCE(MAX(CAST(REPLACE(REPLACE(badgeId, 'CV-', ''), 'CV', '') AS UNSIGNED)), 0) FROM mkt2_gafetes_Casco");
        exec_sql($db, "INSERT INTO mkt2_trip_counters_Casco (counterName, lastNumber)
            VALUES ('badges', ?)
            ON DUPLICATE KEY UPDATE lastNumber=GREATEST(lastNumber, VALUES(lastNumber))", [$seed]);
    }
    exec_sql($db, "UPDATE mkt2_trip_counters_Casco SET lastNumber=LAST_INSERT_ID(lastNumber + 1) WHERE counterName='badges'");
    return (int)scalar($db, "SELECT LAST_INSERT_ID()");
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
    $result = [];
    $seen = [];
    foreach ($items as $item) {
        $badge = clean($item);
        $key = strtoupper($badge);
        if ($badge !== '' && !isset($seen[$key])) {
            $seen[$key] = true;
            $result[] = $badge;
        }
    }
    return $result;
}

function normalize_badges_input(mixed $badgeIds, mixed $badgeId): array
{
    $items = [];
    if (is_array($badgeIds)) {
        foreach ($badgeIds as $value) {
            $items = array_merge($items, split_badges($value));
        }
    }
    $items = array_merge($items, split_badges($badgeId));
    $result = [];
    $seen = [];
    foreach ($items as $item) {
        $key = strtoupper($item);
        if (!isset($seen[$key])) {
            $seen[$key] = true;
            $result[] = $item;
        }
    }
    return $result;
}

function sync_taxista_badges(PDO $db, int $taxistaId, string $taxistaName, array $badgeIds, bool $active): void
{
    if (!$badgeIds) {
        return;
    }
    foreach ($badgeIds as $badgeId) {
        upsert($db, 'mkt2_gafetes_Casco', ['badgeId', 'cycle'], [
            'badgeId' => $badgeId,
            'barcode' => $badgeId,
            'status' => $active ? 'Asignado' : 'Disponible',
            'cycle' => 1,
            'taxistaId' => $taxistaId,
            'taxistaName' => $taxistaName,
            'createdAt' => date('Y-m-d H:i:s'),
            'syncStatus' => 'pending',
            'source' => 'hostinger',
        ]);
    }
}

function find_catalog_by_linked_gafete(PDO $db, string $badge): ?array
{
    return row($db, "SELECT
            c.catalogId,
            COALESCE(NULLIF(GROUP_CONCAT(DISTINCT g.badgeId ORDER BY g.badgeId SEPARATOR ', '), ''), c.badgeId) AS badgeId,
            c.driverName,
            c.phoneNumber,
            c.plate,
            c.vehicleModel,
            c.unitNumber,
            c.serviceType,
            c.site,
            c.hotel,
            c.notes,
            c.suggestedAmount
        FROM mkt2_gafetes_Casco exact
        INNER JOIN mkt2_catalog_taxis_Casco c ON c.catalogId=exact.taxistaId
        LEFT JOIN mkt2_gafetes_Casco g ON g.taxistaId=c.catalogId
        WHERE c.active=1 AND exact.badgeId=?
        GROUP BY c.catalogId,c.badgeId,c.driverName,c.phoneNumber,c.plate,c.vehicleModel,c.unitNumber,c.serviceType,c.site,c.hotel,c.notes,c.suggestedAmount
        LIMIT 1", [$badge]);
}

function request_path(): string
{
    $path = parse_url($_SERVER['REQUEST_URI'], PHP_URL_PATH) ?: '/';

    $apiPos = strpos($path, '/api/');
    if ($apiPos !== false) {
        return substr($path, $apiPos) ?: '/';
    }
    if (str_ends_with($path, '/health')) {
        return '/health';
    }
    if (preg_match('#/(index\.php)?$#', $path) === 1) {
        $trimmed = rtrim($path, '/');
        if ($trimmed === '' || str_ends_with($trimmed, '/casco-api') || str_ends_with($trimmed, '/public_html/casco-api')) {
            return '/';
        }
    }

    $candidatePrefixes = [];
    foreach ([
        $_SERVER['SCRIPT_NAME'] ?? '',
        $_SERVER['PHP_SELF'] ?? '',
    ] as $scriptValue) {
        $scriptDir = rtrim(str_replace('\\', '/', dirname((string)$scriptValue)), '/');
        if ($scriptDir !== '' && $scriptDir !== '/' && !in_array($scriptDir, $candidatePrefixes, true)) {
            $candidatePrefixes[] = $scriptDir;
        }
    }

    foreach (['/casco-api', '/public_html/casco-api'] as $knownPrefix) {
        if (!in_array($knownPrefix, $candidatePrefixes, true)) {
            $candidatePrefixes[] = $knownPrefix;
        }
    }

    foreach ($candidatePrefixes as $prefix) {
        if (str_starts_with($path, $prefix . '/')) {
            $path = substr($path, strlen($prefix)) ?: '/';
            break;
        }
    }

    return '/' . trim($path, '/');
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
    $quoted = array_map(fn($column) => "`$column`", $columns);
    $placeholders = array_fill(0, count($columns), '?');
    $updates = array_values(array_filter($columns, fn($column) => !in_array($column, $keys, true)));
    $updateSql = implode(', ', array_map(fn($column) => "`$column`=VALUES(`$column`)", $updates));
    $sql = "INSERT INTO `$table` (" . implode(',', $quoted) . ") VALUES (" . implode(',', $placeholders) . ") ON DUPLICATE KEY UPDATE $updateSql";
    exec_sql($db, $sql, array_values($data));
}

function normalize_row(array $row): array
{
    foreach ($row as $key => $value) {
        if (is_string($value)) {
            $row[$key] = trim($value);
        }
        if (in_array($key, ['active', 'canViewHome', 'canViewRegister', 'canViewTaxistas', 'canViewHistory', 'canViewUsers', 'canViewAllBranches'], true)) {
            $row[$key] = (bool)$value;
        }
        if ($value !== null && (str_ends_with(strtolower($key), 'date') || in_array($key, ['createdAt', 'updatedAt'], true))) {
            $row[$key] = date('c', strtotime((string)$value));
        }
    }
    return $row;
}

function query(string $key): ?string
{
    return isset($_GET[$key]) ? (string)$_GET[$key] : null;
}

function body(): array
{
    $data = json_decode(file_get_contents('php://input') ?: '', true);
    return is_array($data) ? $data : [];
}

function clean(mixed $value): string
{
    return trim((string)($value ?? ''));
}

function mysql_datetime(mixed $value): string
{
    return date('Y-m-d H:i:s', strtotime(clean($value) ?: 'now'));
}

function parse_bool(mixed $value): bool
{
    return $value === true || $value === 1 || strtolower((string)$value) === 'true' || (string)$value === '1';
}

function valid_text_sql(string $column): string
{
    return "TRIM($column)<>'' AND CHAR_LENGTH(TRIM($column))>1 AND TRIM($column) REGEXP '[[:alpha:]]'";
}

function json_response(mixed $data, int $status = 200): never
{
    http_response_code($status);
    echo json_encode($data, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
    exit;
}
