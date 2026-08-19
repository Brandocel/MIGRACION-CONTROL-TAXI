<?php
declare(strict_types=1);

namespace CascoGafetes;

use PDO;
use RuntimeException;

final class GafeteRepository
{
    public function __construct(private PDO $pdo)
    {
    }

    public function syncBatch(string $branchCode, array $items, bool $dryRun = false): array
    {
        $inserted = 0;
        $updated = 0;
        $unchanged = 0;
        $diagnostics = [];

        if (!$dryRun) {
            $this->pdo->beginTransaction();
        }

        try {
            foreach ($items as $item) {
                $existing = $this->findOne($item['badgeId'], (int)$item['cycle']);
                $mappedStatus = $this->mapStatus($item['status']);

                $wouldInsert = $existing === null;
                $wouldUpdate = false;

                if ($existing !== null && !$this->isSame($existing, $item, $mappedStatus)) {
                    $wouldUpdate = true;
                }

                $diagnostics[] = [
                    'table' => 'mkt2_gafetes_Casco',
                    'badgeId' => $item['badgeId'],
                    'statusInput' => $item['status'],
                    'statusMapped' => $mappedStatus,
                    'wouldInsert' => $wouldInsert,
                    'wouldUpdate' => $wouldUpdate,
                ];

                if ($dryRun) {
                    if ($wouldInsert) {
                        $inserted++;
                    } elseif ($wouldUpdate) {
                        $updated++;
                    } else {
                        $unchanged++;
                    }
                    continue;
                }

                if ($existing === null) {
                    $this->insertOne($item, $mappedStatus);
                    $inserted++;
                    continue;
                }

                if ($this->isSame($existing, $item, $mappedStatus)) {
                    $unchanged++;
                    continue;
                }

                $this->updateOne($item, $mappedStatus);
                $updated++;
            }

            if (!$dryRun && $this->pdo->inTransaction()) {
                $this->pdo->commit();
            }
        } catch (\Throwable $exception) {
            if (!$dryRun && $this->pdo->inTransaction()) {
                $this->pdo->rollBack();
            }

            throw new RuntimeException('No se pudo sincronizar el lote de gafetes.', 0, $exception);
        }

        return [
            'inserted' => $inserted,
            'updated' => $updated,
            'unchanged' => $unchanged,
            'diagnostics' => $diagnostics,
        ];
    }

    private function findOne(string $badgeId, int $cycle): ?array
    {
        $sql = 'SELECT badgeId, barcode, status, cycle, taxistaId, taxistaName, createdAt
                FROM mkt2_gafetes_Casco
                WHERE badgeId = :badgeId AND cycle = :cycle
                LIMIT 1';

        $stmt = $this->pdo->prepare($sql);
        $stmt->execute([
            ':badgeId' => $badgeId,
            ':cycle' => $cycle,
        ]);

        $row = $stmt->fetch(PDO::FETCH_ASSOC);
        return $row === false ? null : $row;
    }

    private function insertOne(array $item, string $mappedStatus): void
    {
        $sql = 'INSERT INTO mkt2_gafetes_Casco
                (badgeId, barcode, status, cycle, taxistaId, taxistaName, createdAt)
                VALUES
                (:badgeId, :barcode, :status, :cycle, :taxistaId, :taxistaName, :createdAt)';

        $stmt = $this->pdo->prepare($sql);
        $stmt->execute([
            ':badgeId' => $item['badgeId'],
            ':barcode' => $item['barcode'],
            ':status' => $mappedStatus,
            ':cycle' => $item['cycle'],
            ':taxistaId' => $item['taxistaId'],
            ':taxistaName' => $item['taxistaName'],
            ':createdAt' => $this->toMysqlDateTime($item['createdAt']),
        ]);
    }

    private function updateOne(array $item, string $mappedStatus): void
    {
        $sql = 'UPDATE mkt2_gafetes_Casco
                SET barcode = :barcode,
                    status = :status,
                    taxistaId = :taxistaId,
                    taxistaName = :taxistaName,
                    createdAt = :createdAt
                WHERE badgeId = :badgeId AND cycle = :cycle';

        $stmt = $this->pdo->prepare($sql);
        $stmt->execute([
            ':badgeId' => $item['badgeId'],
            ':barcode' => $item['barcode'],
            ':status' => $mappedStatus,
            ':cycle' => $item['cycle'],
            ':taxistaId' => $item['taxistaId'],
            ':taxistaName' => $item['taxistaName'],
            ':createdAt' => $this->toMysqlDateTime($item['createdAt']),
        ]);
    }

    private function isSame(array $existing, array $item, string $mappedStatus): bool
    {
        $existingCreatedAt = $existing['createdAt'] ?? null;
        $normalizedExistingCreatedAt = $existingCreatedAt === null
            ? null
            : date('Y-m-d H:i:s', strtotime((string)$existingCreatedAt));

        return (string)($existing['barcode'] ?? '') === (string)$item['barcode']
            && (string)($existing['status'] ?? '') === $mappedStatus
            && (string)($existing['taxistaId'] ?? '') === (string)($item['taxistaId'] ?? '')
            && (string)($existing['taxistaName'] ?? '') === (string)$item['taxistaName']
            && (string)($normalizedExistingCreatedAt ?? '') === $this->toMysqlDateTime($item['createdAt']);
    }

    private function mapStatus(string $status): string
    {
        return match (strtoupper(trim($status))) {
            'A' => 'Asignado',
            'R' => 'Disponible',
            'S' => 'Suspendido',
            default => throw new RuntimeException('Estado no soportado.'),
        };
    }

    private function toMysqlDateTime(string $isoDate): string
    {
        return (new \DateTimeImmutable($isoDate))->format('Y-m-d H:i:s');
    }
}
