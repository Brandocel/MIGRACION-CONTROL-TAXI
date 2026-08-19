<?php
declare(strict_types=1);

use CascoGafetes\Database;
use CascoGafetes\GafeteController;
use CascoGafetes\GafeteRepository;
use CascoGafetes\GafeteValidator;

require_once __DIR__ . '/src/Database.php';
require_once __DIR__ . '/src/GafeteValidator.php';
require_once __DIR__ . '/src/GafeteRepository.php';
require_once __DIR__ . '/src/GafeteController.php';

$configFile = __DIR__ . '/config.php';
$exampleConfigFile = __DIR__ . '/config.example.php';
$config = file_exists($configFile) ? require $configFile : require $exampleConfigFile;

$path = parse_url($_SERVER['REQUEST_URI'] ?? '/', PHP_URL_PATH) ?? '/';
$method = strtoupper($_SERVER['REQUEST_METHOD'] ?? 'GET');
$scriptName = $_SERVER['SCRIPT_NAME'] ?? '';
$baseDir = rtrim(str_replace('\\', '/', dirname($scriptName)), '/');

if ($baseDir !== '' && str_starts_with($path, $baseDir)) {
    $path = substr($path, strlen($baseDir));
}

$normalizedPath = '/' . ltrim($path, '/');
$normalizedPath = preg_replace('#/+#', '/', $normalizedPath) ?: '/';

$controller = new GafeteController(
    new GafeteValidator(),
    new GafeteRepository(Database::connect($config)),
    $config
);

if ($method === 'POST' && in_array($normalizedPath, ['/api/gafetes/sync', '/gafetes/sync'], true)) {
    $controller->sync();
    exit;
}

http_response_code(404);
header('Content-Type: application/json; charset=utf-8');
echo json_encode([
    'success' => false,
    'error' => 'Ruta no encontrada',
    'path' => $normalizedPath,
], JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
