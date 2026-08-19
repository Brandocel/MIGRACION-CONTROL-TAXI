CREATE TABLE IF NOT EXISTS mkt2_gafetes (
    id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    branch_code VARCHAR(10) NOT NULL,
    badge_id VARCHAR(50) NOT NULL,
    barcode VARCHAR(100) NULL,
    status CHAR(1) NOT NULL,
    cycle INT NOT NULL DEFAULT 1,
    taxista_id VARCHAR(100) NULL,
    taxista_name VARCHAR(255) NULL,
    source_created_at DATETIME NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    UNIQUE KEY uq_branch_badge_cycle (branch_code, badge_id, cycle),
    INDEX ix_status (status),
    INDEX ix_updated_at (updated_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
