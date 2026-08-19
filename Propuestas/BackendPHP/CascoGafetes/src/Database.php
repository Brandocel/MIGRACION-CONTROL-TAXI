<?php
declare(strict_types=1);

namespace CascoGafetes;

use PDO;
use PDOException;
use RuntimeException;

final class Database
{
    public static function connect(array $config): PDO
    {
        $db = $config['db'] ?? [];
        $host = (string)($db['host'] ?? '');
        $port = (string)($db['port'] ?? '3306');
        $name = (string)($db['name'] ?? '');
        $charset = (string)($db['charset'] ?? 'utf8mb4');
        $user = (string)($db['user'] ?? '');
        $password = (string)($db['password'] ?? '');

        if ($host === '' || $name === '' || $user === '') {
            throw new RuntimeException('Configuracion de base de datos incompleta.');
        }

        $dsn = sprintf('mysql:host=%s;port=%s;dbname=%s;charset=%s', $host, $port, $name, $charset);

        try {
            return new PDO($dsn, $user, $password, [
                PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION,
                PDO::ATTR_DEFAULT_FETCH_MODE => PDO::FETCH_ASSOC,
                PDO::ATTR_EMULATE_PREPARES => false,
            ]);
        } catch (PDOException $exception) {
            throw new RuntimeException('No se pudo conectar a MySQL/MariaDB.', 0, $exception);
        }
    }
}
