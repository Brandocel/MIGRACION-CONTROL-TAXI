<?php
declare(strict_types=1);

namespace CascoGafetes;

use RuntimeException;

final class GafeteController
{
    public function __construct(
        private GafeteValidator $validator,
        private GafeteRepository $repository,
        private array $config
    ) {
    }

    public function sync(): void
    {
        header('Content-Type: application/json; charset=utf-8');

        if (!$this->isJsonRequest()) {
            $this->respond(422, [
                'success' => false,
                'errors' => [[
                    'index' => null,
                    'field' => 'Content-Type',
                    'message' => 'Content-Type debe ser application/json',
                ]],
            ]);
            return;
        }

        if (!$this->isAuthorized()) {
            $this->respond(401, [
                'success' => false,
                'error' => 'Token invalido o faltante',
            ]);
            return;
        }

        $rawBody = file_get_contents('php://input');
        $payload = json_decode($rawBody ?: '', true);

        if (!is_array($payload)) {
            $this->respond(422, [
                'success' => false,
                'errors' => [[
                    'index' => null,
                    'field' => 'body',
                    'message' => 'JSON invalido',
                ]],
            ]);
            return;
        }

        $validation = $this->validator->validate($payload, $this->config);
        if (($validation['valid'] ?? false) !== true) {
            $this->respond((int)$validation['status'], $validation['response']);
            return;
        }

        try {
            $items = $validation['items'];
            $dryRun = isset($_GET['dryRun']) && (string)$_GET['dryRun'] === '1';
            $result = $this->repository->syncBatch($validation['branchCode'], $items, $dryRun);

            if ($dryRun) {
                $first = $result['diagnostics'][0] ?? null;

                $this->respond(200, [
                    'success' => true,
                    'dryRun' => true,
                    'table' => 'mkt2_gafetes_Casco',
                    'branchCode' => $validation['branchCode'],
                    'received' => count($items),
                    'inserted' => $result['inserted'],
                    'updated' => $result['updated'],
                    'unchanged' => $result['unchanged'],
                    'badgeId' => $first['badgeId'] ?? null,
                    'statusInput' => $first['statusInput'] ?? null,
                    'statusMapped' => $first['statusMapped'] ?? null,
                    'wouldInsert' => $first['wouldInsert'] ?? false,
                    'wouldUpdate' => $first['wouldUpdate'] ?? false,
                    'errors' => [],
                ]);
                return;
            }

            $this->respond(200, [
                'success' => true,
                'branchCode' => $validation['branchCode'],
                'received' => count($items),
                'inserted' => $result['inserted'],
                'updated' => $result['updated'],
                'unchanged' => $result['unchanged'],
                'errors' => [],
            ]);
        } catch (RuntimeException) {
            $this->respond(500, [
                'success' => false,
                'error' => 'Error interno',
            ]);
        }
    }

    private function isJsonRequest(): bool
    {
        $contentType = $_SERVER['CONTENT_TYPE'] ?? $_SERVER['HTTP_CONTENT_TYPE'] ?? '';
        return str_contains(strtolower($contentType), 'application/json');
    }

    private function isAuthorized(): bool
    {
        $app = $this->config['app'] ?? [];
        $environment = (string)($app['environment'] ?? 'development');
        $configuredToken = $app['sync_token'] ?? getenv('SYNC_TOKEN') ?: null;
        $requireToken = (bool)($app['require_token_in_production'] ?? true);

        if ($environment !== 'production' && empty($configuredToken)) {
            return true;
        }

        if ($environment === 'production' && empty($configuredToken) && $requireToken) {
            return false;
        }

        if (empty($configuredToken)) {
            return true;
        }

        $providedToken = $_SERVER['HTTP_X_SYNC_TOKEN'] ?? '';
        return hash_equals((string)$configuredToken, (string)$providedToken);
    }

    private function respond(int $status, array $body): void
    {
        http_response_code($status);
        echo json_encode($body, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
    }
}
