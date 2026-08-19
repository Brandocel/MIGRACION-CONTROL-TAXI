-- Catalogo de unidades para Control Taxi.
-- No ejecuta migracion de datos. Solo crea estructura base idempotente.
-- Uso esperado:
--   1. La dejada real vive en mkt2_catalog_units.defaultPayoutAmount.
--   2. El catalogo de taxis se relaciona por mkt2_catalog_taxi_units.
--   3. No guardar la dejada como campo plano del taxista.

CREATE TABLE IF NOT EXISTS mkt2_catalog_units (
    unitId BIGINT NOT NULL AUTO_INCREMENT,
    branchCode VARCHAR(50) NOT NULL,
    unitNumber VARCHAR(80) NOT NULL,
    plate VARCHAR(80) NOT NULL DEFAULT '',
    vehicleModel VARCHAR(180) NOT NULL DEFAULT '',
    serviceType VARCHAR(120) NOT NULL DEFAULT '',
    site VARCHAR(180) NOT NULL DEFAULT '',
    hotel VARCHAR(220) NOT NULL DEFAULT '',
    defaultPayoutAmount DECIMAL(18,2) NOT NULL DEFAULT 0,
    notes VARCHAR(700) NOT NULL DEFAULT '',
    active TINYINT(1) NOT NULL DEFAULT 1,
    createdAt DATETIME NULL DEFAULT CURRENT_TIMESTAMP,
    updatedAt DATETIME NULL DEFAULT NULL,
    PRIMARY KEY (unitId),
    UNIQUE KEY ux_catalog_units_branch_unit (branchCode, unitNumber),
    KEY ix_catalog_units_search (branchCode, active, unitNumber, serviceType, plate)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS mkt2_catalog_taxi_units (
    relationId BIGINT NOT NULL AUTO_INCREMENT,
    branchCode VARCHAR(50) NOT NULL,
    catalogId BIGINT NOT NULL,
    unitId BIGINT NOT NULL,
    active TINYINT(1) NOT NULL DEFAULT 1,
    createdAt DATETIME NULL DEFAULT CURRENT_TIMESTAMP,
    updatedAt DATETIME NULL DEFAULT NULL,
    PRIMARY KEY (relationId),
    UNIQUE KEY ux_catalog_taxi_units_active (branchCode, catalogId, unitId),
    KEY ix_catalog_taxi_units_taxi (branchCode, catalogId, active),
    KEY ix_catalog_taxi_units_unit (branchCode, unitId, active)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

