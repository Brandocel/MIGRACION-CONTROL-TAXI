<?php
declare(strict_types=1);

return [
    'db' => [
        'host' => getenv('DB_HOST') ?: '127.0.0.1',
        'port' => getenv('DB_PORT') ?: '3306',
        'name' => getenv('DB_NAME') ?: 'database_name',
        'user' => getenv('DB_USER') ?: 'database_user',
        'password' => getenv('DB_PASSWORD') ?: 'database_password',
        'charset' => getenv('DB_CHARSET') ?: 'utf8mb4',
    ],
    'app' => [
        'environment' => getenv('ENVIRONMENT') ?: 'development',
        'required_branch_code' => 'CV',
        'reject_branch_codes' => ['28'],
        'max_batch_size' => 500,
        'require_token_in_production' => true,
        'sync_token' => getenv('SYNC_TOKEN') ?: null,
    ],
];
