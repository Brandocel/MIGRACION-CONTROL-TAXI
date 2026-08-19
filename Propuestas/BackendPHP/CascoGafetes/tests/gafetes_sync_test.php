<?php
declare(strict_types=1);

require_once __DIR__ . '/../src/GafeteValidator.php';

use CascoGafetes\GafeteValidator;

$validator = new GafeteValidator();
$config = require __DIR__ . '/../config.example.php';

function assertTrue(bool $condition, string $label): void
{
    if (!$condition) {
        fwrite(STDERR, "[FAIL] $label" . PHP_EOL);
        exit(1);
    }

    fwrite(STDOUT, "[OK] $label" . PHP_EOL);
}

$validPayload = [
    'branchCode' => 'CV',
    'gafetes' => [[
        'badgeId' => '2121',
        'barcode' => '2121',
        'status' => 'R',
        'cycle' => 1,
        'taxistaId' => null,
        'taxistaName' => '',
        'createdAt' => '2026-07-18T11:31:18',
    ]],
];

$result = $validator->validate($validPayload, $config);
assertTrue($result['valid'] === true, 'CV valido con R');

$result = $validator->validate($validPayload, $config);
assertTrue($result['valid'] === true, 'Reenvio identico sigue siendo valido para la capa de validacion');

$updatedPayload = $validPayload;
$updatedPayload['gafetes'][0]['status'] = 'A';
$result = $validator->validate($updatedPayload, $config);
assertTrue($result['valid'] === true, 'Actualizacion R a A valida');

$invalidStatus = $validPayload;
$invalidStatus['gafetes'][0]['status'] = 'X';
$result = $validator->validate($invalidStatus, $config);
assertTrue($result['valid'] === false && $result['status'] === 422, 'Status invalido');

$branch28 = $validPayload;
$branch28['branchCode'] = '28';
$result = $validator->validate($branch28, $config);
assertTrue($result['valid'] === false && $result['status'] === 400, 'BranchCode 28 rechazado');

$emptyBranch = $validPayload;
$emptyBranch['branchCode'] = '';
$result = $validator->validate($emptyBranch, $config);
assertTrue($result['valid'] === false && $result['status'] === 400, 'BranchCode vacio rechazado');

$emptyPayload = ['branchCode' => 'CV', 'gafetes' => []];
$result = $validator->validate($emptyPayload, $config);
assertTrue($result['valid'] === false && $result['status'] === 422, 'Payload vacio rechazado');

$missingBadge = $validPayload;
$missingBadge['gafetes'][0]['badgeId'] = '';
$result = $validator->validate($missingBadge, $config);
assertTrue($result['valid'] === false && $result['status'] === 422, 'badgeId faltante');

$cycleZero = $validPayload;
$cycleZero['gafetes'][0]['cycle'] = 0;
$result = $validator->validate($cycleZero, $config);
assertTrue($result['valid'] === false && $result['status'] === 422, 'cycle 0 rechazado');

$twoBadges = $validPayload;
$twoBadges['gafetes'][] = [
    'badgeId' => '24',
    'barcode' => '24',
    'status' => 'A',
    'cycle' => 1,
    'taxistaId' => null,
    'taxistaName' => '',
    'createdAt' => '2026-07-18T11:31:18',
];
$result = $validator->validate($twoBadges, $config);
assertTrue($result['valid'] === true && count($result['items']) === 2, 'Dos gafetes validos');

$duplicateInBatch = $validPayload;
$duplicateInBatch['gafetes'][] = $duplicateInBatch['gafetes'][0];
$result = $validator->validate($duplicateInBatch, $config);
assertTrue($result['valid'] === false && $result['status'] === 422, 'Duplicado dentro del lote rechazado');

fwrite(STDOUT, PHP_EOL);
fwrite(STDOUT, "Prueba manual sugerida para token invalido:" . PHP_EOL);
fwrite(STDOUT, "1. Configurar ENVIRONMENT=production y SYNC_TOKEN en config.php" . PHP_EOL);
fwrite(STDOUT, "2. Hacer curl con X-Sync-Token incorrecto" . PHP_EOL);
fwrite(STDOUT, "3. Esperar HTTP 401" . PHP_EOL);
