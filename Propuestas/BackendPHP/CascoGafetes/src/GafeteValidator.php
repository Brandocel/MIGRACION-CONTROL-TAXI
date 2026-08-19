<?php
declare(strict_types=1);

namespace CascoGafetes;

final class GafeteValidator
{
    public function validate(array $payload, array $config): array
    {
        $errors = [];
        $requiredBranchCode = (string)($config['app']['required_branch_code'] ?? 'CV');
        $rejectedBranches = $config['app']['reject_branch_codes'] ?? ['28'];
        $maxBatchSize = (int)($config['app']['max_batch_size'] ?? 500);

        $branchCode = trim((string)($payload['branchCode'] ?? ''));
        if ($branchCode === '') {
            return ['valid' => false, 'status' => 400, 'response' => [
                'success' => false,
                'error' => 'branchCode debe ser CV',
            ]];
        }

        if (in_array($branchCode, $rejectedBranches, true) || $branchCode !== $requiredBranchCode) {
            return ['valid' => false, 'status' => 400, 'response' => [
                'success' => false,
                'error' => 'branchCode debe ser CV',
            ]];
        }

        $items = $payload['gafetes'] ?? $payload['mkt2_gafetes'] ?? null;
        if (!is_array($items) || $items === []) {
            return ['valid' => false, 'status' => 422, 'response' => [
                'success' => false,
                'errors' => [[
                    'index' => null,
                    'field' => 'gafetes',
                    'message' => 'El lote de gafetes no puede estar vacio',
                ]],
            ]];
        }

        if (count($items) > $maxBatchSize) {
            return ['valid' => false, 'status' => 422, 'response' => [
                'success' => false,
                'errors' => [[
                    'index' => null,
                    'field' => 'gafetes',
                    'message' => 'El lote excede el maximo permitido',
                ]],
            ]];
        }

        $normalized = [];
        $seen = [];

        foreach ($items as $index => $item) {
            if (!is_array($item)) {
                $errors[] = ['index' => $index, 'field' => 'gafete', 'message' => 'Formato invalido'];
                continue;
            }

            $badgeId = trim((string)($item['badgeId'] ?? ''));
            $barcode = trim((string)($item['barcode'] ?? ''));
            $status = strtoupper(trim((string)($item['status'] ?? '')));
            $cycle = $item['cycle'] ?? null;
            $taxistaId = $item['taxistaId'] ?? null;
            $taxistaName = trim((string)($item['taxistaName'] ?? ''));
            $createdAt = trim((string)($item['createdAt'] ?? ''));

            if ($badgeId === '') {
                $errors[] = ['index' => $index, 'field' => 'badgeId', 'message' => 'badgeId es obligatorio'];
            }

            if ($barcode === '') {
                $errors[] = ['index' => $index, 'field' => 'barcode', 'message' => 'barcode es obligatorio'];
            }

            if (!in_array($status, ['A', 'R', 'S'], true)) {
                $errors[] = ['index' => $index, 'field' => 'status', 'message' => 'Valor invalido'];
            }

            if (!is_int($cycle) && !(is_string($cycle) && ctype_digit($cycle))) {
                $errors[] = ['index' => $index, 'field' => 'cycle', 'message' => 'cycle debe ser entero >= 1'];
            } elseif ((int)$cycle < 1) {
                $errors[] = ['index' => $index, 'field' => 'cycle', 'message' => 'cycle debe ser entero >= 1'];
            }

            if ($createdAt === '' || !$this->isIsoDate($createdAt)) {
                $errors[] = ['index' => $index, 'field' => 'createdAt', 'message' => 'createdAt debe ser una fecha ISO valida'];
            }

            if ($taxistaName === '') {
                $errors[] = ['index' => $index, 'field' => 'taxistaName', 'message' => 'taxistaName es obligatorio'];
            }

            $dedupeKey = $badgeId . '|' . (string)(int)$cycle;
            if ($badgeId !== '' && isset($seen[$dedupeKey])) {
                $errors[] = ['index' => $index, 'field' => 'badgeId', 'message' => 'Duplicado dentro del lote'];
            } else {
                $seen[$dedupeKey] = true;
            }

            $normalized[] = [
                'branchCode' => $branchCode,
                'badgeId' => $badgeId,
                'barcode' => $barcode,
                'status' => $status,
                'cycle' => (int)$cycle,
                'taxistaId' => ($taxistaId === '' || $taxistaId === null) ? null : (int)$taxistaId,
                'taxistaName' => $taxistaName,
                'createdAt' => $createdAt,
            ];
        }

        if ($errors !== []) {
            return ['valid' => false, 'status' => 422, 'response' => [
                'success' => false,
                'errors' => $errors,
            ]];
        }

        return [
            'valid' => true,
            'status' => 200,
            'branchCode' => $branchCode,
            'items' => $normalized,
        ];
    }

    private function isIsoDate(string $value): bool
    {
        try {
            $date = new \DateTimeImmutable($value);
            return $date->format('c') !== '';
        } catch (\Throwable) {
            return false;
        }
    }
}
