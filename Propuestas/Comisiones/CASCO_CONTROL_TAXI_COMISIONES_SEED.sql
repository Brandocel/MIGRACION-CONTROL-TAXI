/*
    Carga inicial de reglas de comisiones para Casco Viejo.
    Fuente: CALCULO DE COMISIONES, PTO MORELOS (1).xlsx / hoja CASCO
    Esta carga no activa calculos en la aplicacion. Todas las reglas quedan Activo = 0.
*/

IF OBJECT_ID(N'dbo.ControlTaxiComisiones', N'U') IS NULL
BEGIN
    RAISERROR(N'Primero ejecuta CASCO_CONTROL_TAXI_COMISIONES.sql para crear/ajustar dbo.ControlTaxiComisiones.', 16, 1);
    RETURN;
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM dbo.ControlTaxiComisiones
    WHERE BranchCode = N'CV'
      AND Proveedor = N'BIKE CID'
      AND TipoServicio = N'GENERAL'
      AND ConTarjeta = 1
      AND VentaMinima = 0
      AND VentaMaxima IS NULL
)
BEGIN
    INSERT INTO dbo.ControlTaxiComisiones
    (
        BranchCode, Proveedor, TipoServicio, ConTarjeta, VentaMinima, VentaMaxima,
        ComisionAgencia, ComisionTaxista, ComisionVendedor, ComisionDeportiva,
        AplicaDegustacion, AplicaGasto, Activo, ReglaNombre, OrigenExcel,
        RequiereValidacion, Observaciones
    )
    VALUES
    (
        N'CV', N'BIKE CID', N'GENERAL', 1, 0, NULL,
        0.1900, NULL, NULL, 40.0000,
        1, 1, 0, N'BIKE CID C/TARJETA', N'CASCO!A2:B15',
        1, N'Excel indica 19% agencia, 10% y texto "40 o 45 (meta)". Validar si el 10% corresponde a taxista o vendedor y si la deportiva es 40 o 45.'
    );
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM dbo.ControlTaxiComisiones
    WHERE BranchCode = N'CV'
      AND Proveedor = N'BIKE CID'
      AND TipoServicio = N'GENERAL'
      AND ConTarjeta = 0
      AND VentaMinima = 0
      AND VentaMaxima IS NULL
)
BEGIN
    INSERT INTO dbo.ControlTaxiComisiones
    (
        BranchCode, Proveedor, TipoServicio, ConTarjeta, VentaMinima, VentaMaxima,
        ComisionAgencia, ComisionTaxista, ComisionVendedor, ComisionDeportiva,
        AplicaDegustacion, AplicaGasto, Activo, ReglaNombre, OrigenExcel,
        RequiereValidacion, Observaciones
    )
    VALUES
    (
        N'CV', N'BIKE CID', N'GENERAL', 0, 0, NULL,
        NULL, NULL, NULL, 40.0000,
        1, 1, 0, N'BIKE CID S/TARJETA', N'CASCO!C2:D15',
        1, N'Excel solo deja claro dejada, gasto, degustacion, 10% y "40 o 45 (meta)". Validar reparto exacto.'
    );
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM dbo.ControlTaxiComisiones
    WHERE BranchCode = N'CV'
      AND Proveedor = N'TAXIS/VANS'
      AND TipoServicio = N'GENERAL'
      AND ConTarjeta = 1
      AND VentaMinima = 0
      AND VentaMaxima IS NULL
)
BEGIN
    INSERT INTO dbo.ControlTaxiComisiones
    (
        BranchCode, Proveedor, TipoServicio, ConTarjeta, VentaMinima, VentaMaxima,
        ComisionAgencia, ComisionTaxista, ComisionVendedor, ComisionDeportiva,
        AplicaDegustacion, AplicaGasto, Activo, ReglaNombre, OrigenExcel,
        RequiereValidacion, Observaciones
    )
    VALUES
    (
        N'CV', N'TAXIS/VANS', N'GENERAL', 1, 0, NULL,
        0.1900, 0.1000, NULL, 40.0000,
        1, 1, 0, N'TAXIS/VANS C/TARJETA', N'CASCO!G2:H16',
        1, N'Excel muestra 19% y 10%, mas "40 o 45 (meta)". Validar si deportiva debe quedar en 40 o 45 y si aplica comision de vendedor.'
    );
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM dbo.ControlTaxiComisiones
    WHERE BranchCode = N'CV'
      AND Proveedor = N'TAXIS/VANS'
      AND TipoServicio = N'GENERAL'
      AND ConTarjeta = 0
      AND VentaMinima = 0
      AND VentaMaxima IS NULL
)
BEGIN
    INSERT INTO dbo.ControlTaxiComisiones
    (
        BranchCode, Proveedor, TipoServicio, ConTarjeta, VentaMinima, VentaMaxima,
        ComisionAgencia, ComisionTaxista, ComisionVendedor, ComisionDeportiva,
        AplicaDegustacion, AplicaGasto, Activo, ReglaNombre, OrigenExcel,
        RequiereValidacion, Observaciones
    )
    VALUES
    (
        N'CV', N'TAXIS/VANS', N'GENERAL', 0, 0, NULL,
        NULL, 0.1000, NULL, 40.0000,
        1, 1, 0, N'TAXIS/VANS S/TARJETA', N'CASCO!I2:J16',
        1, N'Excel confirma 10% y las banderas de gasto/degustacion, pero no deja claro si existe porcentaje de agencia sin tarjeta.'
    );
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM dbo.ControlTaxiComisiones
    WHERE BranchCode = N'CV'
      AND Proveedor = N'CALLE'
      AND TipoServicio = N'GENERAL'
      AND ConTarjeta = 1
      AND VentaMinima = 0
      AND VentaMaxima IS NULL
)
BEGIN
    INSERT INTO dbo.ControlTaxiComisiones
    (
        BranchCode, Proveedor, TipoServicio, ConTarjeta, VentaMinima, VentaMaxima,
        ComisionAgencia, ComisionTaxista, ComisionVendedor, ComisionDeportiva,
        AplicaDegustacion, AplicaGasto, Activo, ReglaNombre, OrigenExcel,
        RequiereValidacion, Observaciones
    )
    VALUES
    (
        N'CV', N'CALLE', N'GENERAL', 1, 0, NULL,
        0.1900, NULL, NULL, 40.0000,
        1, 1, 0, N'CALLE C/TARJETA', N'CASCO!L2:M12',
        1, N'Excel muestra 19%, degustacion, gastos y deportiva "40 o 45 (meta)". Validar si existe porcentaje adicional para taxista o vendedor.'
    );
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM dbo.ControlTaxiComisiones
    WHERE BranchCode = N'CV'
      AND Proveedor = N'CALLE'
      AND TipoServicio = N'GENERAL'
      AND ConTarjeta = 0
      AND VentaMinima = 0
      AND VentaMaxima IS NULL
)
BEGIN
    INSERT INTO dbo.ControlTaxiComisiones
    (
        BranchCode, Proveedor, TipoServicio, ConTarjeta, VentaMinima, VentaMaxima,
        ComisionAgencia, ComisionTaxista, ComisionVendedor, ComisionDeportiva,
        AplicaDegustacion, AplicaGasto, Activo, ReglaNombre, OrigenExcel,
        RequiereValidacion, Observaciones
    )
    VALUES
    (
        N'CV', N'CALLE', N'GENERAL', 0, 0, NULL,
        NULL, NULL, NULL, 40.0000,
        1, 1, 0, N'CALLE S/TARJETA', N'CASCO!N2:O12',
        1, N'Excel muestra degustacion/gastos sin porcentaje claramente asociado. Validar reparto exacto.'
    );
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM dbo.ControlTaxiComisiones
    WHERE BranchCode = N'CV'
      AND Proveedor = N'EXTREME'
      AND TipoServicio = N'GENERAL'
      AND ConTarjeta = 1
      AND VentaMinima = 0
      AND VentaMaxima IS NULL
)
BEGIN
    INSERT INTO dbo.ControlTaxiComisiones
    (
        BranchCode, Proveedor, TipoServicio, ConTarjeta, VentaMinima, VentaMaxima,
        ComisionAgencia, ComisionTaxista, ComisionVendedor, ComisionDeportiva,
        AplicaDegustacion, AplicaGasto, Activo, ReglaNombre, OrigenExcel,
        RequiereValidacion, Observaciones
    )
    VALUES
    (
        N'CV', N'EXTREME', N'GENERAL', 1, 0, NULL,
        NULL, 0.1000, NULL, 40.0000,
        1, 1, 0, N'EXTREME C/TARJETA', N'CASCO!A19:B32',
        1, N'Excel tiene 19% y 10%, pero el total visible habla de "COM GUIA". Validar si 19% corresponde a agencia.'
    );
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM dbo.ControlTaxiComisiones
    WHERE BranchCode = N'CV'
      AND Proveedor = N'EXTREME'
      AND TipoServicio = N'GENERAL'
      AND ConTarjeta = 0
      AND VentaMinima = 0
      AND VentaMaxima IS NULL
)
BEGIN
    INSERT INTO dbo.ControlTaxiComisiones
    (
        BranchCode, Proveedor, TipoServicio, ConTarjeta, VentaMinima, VentaMaxima,
        ComisionAgencia, ComisionTaxista, ComisionVendedor, ComisionDeportiva,
        AplicaDegustacion, AplicaGasto, Activo, ReglaNombre, OrigenExcel,
        RequiereValidacion, Observaciones
    )
    VALUES
    (
        N'CV', N'EXTREME', N'GENERAL', 0, 0, NULL,
        NULL, 0.1000, NULL, NULL,
        1, 1, 0, N'EXTREME S/TARJETA', N'CASCO!C19:D32',
        1, N'La hoja no deja claro si la rama sin tarjeta conserva 19% de agencia ni si aplica deportiva.'
    );
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM dbo.ControlTaxiComisiones
    WHERE BranchCode = N'CV'
      AND Proveedor = N'AVENTURAS MAYAS'
      AND TipoServicio = N'GENERAL'
      AND ConTarjeta = 1
      AND VentaMinima = 1000
      AND VentaMaxima IS NULL
)
BEGIN
    INSERT INTO dbo.ControlTaxiComisiones
    (
        BranchCode, Proveedor, TipoServicio, ConTarjeta, VentaMinima, VentaMaxima,
        ComisionAgencia, ComisionTaxista, ComisionVendedor, ComisionDeportiva,
        AplicaDegustacion, AplicaGasto, Activo, ReglaNombre, OrigenExcel,
        RequiereValidacion, Observaciones
    )
    VALUES
    (
        N'CV', N'AVENTURAS MAYAS', N'GENERAL', 1, 1000, NULL,
        0.0200, 0.1000, NULL, 35.0000,
        1, 1, 0, N'AVENTURAS MAYAS C/TARJETA + META', N'CASCO!G19:H38',
        1, N'Excel indica ventas arriba de 1000, descuento de 100 por cada 1000, 10%, 2% y deportiva 35%. Requiere validacion del orden de calculo.'
    );
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM dbo.ControlTaxiComisiones
    WHERE BranchCode = N'CV'
      AND Proveedor = N'AVENTURAS MAYAS'
      AND TipoServicio = N'GENERAL'
      AND ConTarjeta = 0
      AND VentaMinima = 1000
      AND VentaMaxima IS NULL
)
BEGIN
    INSERT INTO dbo.ControlTaxiComisiones
    (
        BranchCode, Proveedor, TipoServicio, ConTarjeta, VentaMinima, VentaMaxima,
        ComisionAgencia, ComisionTaxista, ComisionVendedor, ComisionDeportiva,
        AplicaDegustacion, AplicaGasto, Activo, ReglaNombre, OrigenExcel,
        RequiereValidacion, Observaciones
    )
    VALUES
    (
        N'CV', N'AVENTURAS MAYAS', N'GENERAL', 0, 1000, NULL,
        0.0200, 0.1000, NULL, 35.0000,
        1, 1, 0, N'AVENTURAS MAYAS S/TARJETA + META', N'CASCO!I19:J38',
        1, N'Misma observacion que con tarjeta; la hoja no aclara diferencias claras entre ramas, pero mantiene bloques separados.'
    );
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM dbo.ControlTaxiComisiones
    WHERE BranchCode = N'CV'
      AND Proveedor = N'MAJESTIC'
      AND TipoServicio = N'GENERAL'
      AND ConTarjeta = 1
      AND VentaMinima = 0
      AND VentaMaxima IS NULL
)
BEGIN
    INSERT INTO dbo.ControlTaxiComisiones
    (
        BranchCode, Proveedor, TipoServicio, ConTarjeta, VentaMinima, VentaMaxima,
        ComisionAgencia, ComisionTaxista, ComisionVendedor, ComisionDeportiva,
        AplicaDegustacion, AplicaGasto, Activo, ReglaNombre, OrigenExcel,
        RequiereValidacion, Observaciones
    )
    VALUES
    (
        N'CV', N'MAJESTIC', N'GENERAL', 1, 0, NULL,
        0.0400, 0.0800, NULL, 30.0000,
        1, 1, 0, N'MAJESTIC C/TARJETA', N'CASCO!A39:B50',
        0, N'Regla clara: 8% guia, 4% agencia, deportiva 30, gasto y degustacion.'
    );
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM dbo.ControlTaxiComisiones
    WHERE BranchCode = N'CV'
      AND Proveedor = N'MAJESTIC'
      AND TipoServicio = N'GENERAL'
      AND ConTarjeta = 0
      AND VentaMinima = 0
      AND VentaMaxima IS NULL
)
BEGIN
    INSERT INTO dbo.ControlTaxiComisiones
    (
        BranchCode, Proveedor, TipoServicio, ConTarjeta, VentaMinima, VentaMaxima,
        ComisionAgencia, ComisionTaxista, ComisionVendedor, ComisionDeportiva,
        AplicaDegustacion, AplicaGasto, Activo, ReglaNombre, OrigenExcel,
        RequiereValidacion, Observaciones
    )
    VALUES
    (
        N'CV', N'MAJESTIC', N'GENERAL', 0, 0, NULL,
        0.0400, 0.0800, NULL, 30.0000,
        1, 1, 0, N'MAJESTIC S/TARJETA', N'CASCO!C39:D50',
        0, N'Regla clara: 8% guia, 4% agencia, deportiva 30, gasto y degustacion.'
    );
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM dbo.ControlTaxiComisiones
    WHERE BranchCode = N'CV'
      AND Proveedor = N'TAXIS Y GUIAS IND (FARMACIAS)'
      AND TipoServicio = N'ANTIBIOTICOS'
      AND ConTarjeta = 1
      AND VentaMinima = 0
      AND VentaMaxima IS NULL
)
BEGIN
    INSERT INTO dbo.ControlTaxiComisiones
    (
        BranchCode, Proveedor, TipoServicio, ConTarjeta, VentaMinima, VentaMaxima,
        ComisionAgencia, ComisionTaxista, ComisionVendedor, ComisionDeportiva,
        AplicaDegustacion, AplicaGasto, Activo, ReglaNombre, OrigenExcel,
        RequiereValidacion, Observaciones
    )
    VALUES
    (
        N'CV', N'TAXIS Y GUIAS IND (FARMACIAS)', N'ANTIBIOTICOS', 1, 0, NULL,
        0.1900, NULL, NULL, 40.0000,
        0, 1, 0, N'FARMACIAS C/TARJETA ANTIBIOTICOS', N'CASCO!L39:M50',
        1, N'La hoja anota 10% antibioticos y controlados 20%, ademas de 19%, dejada/gasto y meta 40 o 45. Validar distribucion exacta.'
    );
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM dbo.ControlTaxiComisiones
    WHERE BranchCode = N'CV'
      AND Proveedor = N'TAXIS Y GUIAS IND (FARMACIAS)'
      AND TipoServicio = N'CONTROLADOS'
      AND ConTarjeta = 0
      AND VentaMinima = 0
      AND VentaMaxima IS NULL
)
BEGIN
    INSERT INTO dbo.ControlTaxiComisiones
    (
        BranchCode, Proveedor, TipoServicio, ConTarjeta, VentaMinima, VentaMaxima,
        ComisionAgencia, ComisionTaxista, ComisionVendedor, ComisionDeportiva,
        AplicaDegustacion, AplicaGasto, Activo, ReglaNombre, OrigenExcel,
        RequiereValidacion, Observaciones
    )
    VALUES
    (
        N'CV', N'TAXIS Y GUIAS IND (FARMACIAS)', N'CONTROLADOS', 0, 0, NULL,
        NULL, NULL, NULL, 40.0000,
        0, 1, 0, N'FARMACIAS S/TARJETA CONTROLADOS', N'CASCO!N39:O50',
        1, N'La hoja solo indica "20% y/o 10%" y "40 o 45 (meta)". Validar si el 20% aplica a controlados, quien la recibe y si existe rama de agencia.'
    );
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM dbo.ControlTaxiComisiones
    WHERE BranchCode = N'CV'
      AND Proveedor = N'VENTAS ENTRE TIENDAS'
      AND TipoServicio = N'INTERDEPARTAMENTAL'
      AND ConTarjeta = 1
      AND VentaMinima = 0
      AND VentaMaxima IS NULL
)
BEGIN
    INSERT INTO dbo.ControlTaxiComisiones
    (
        BranchCode, Proveedor, TipoServicio, ConTarjeta, VentaMinima, VentaMaxima,
        ComisionAgencia, ComisionTaxista, ComisionVendedor, ComisionDeportiva,
        AplicaDegustacion, AplicaGasto, Activo, ReglaNombre, OrigenExcel,
        RequiereValidacion, Observaciones
    )
    VALUES
    (
        N'CV', N'VENTAS ENTRE TIENDAS', N'INTERDEPARTAMENTAL', 1, 0, NULL,
        0.1900, NULL, 0.2000, 50.0000,
        1, 1, 0, N'VENTAS ENTRE TIENDAS C/TARJETA', N'CASCO!G42:H50',
        1, N'El Excel indica 50%, "comision Matilde", 20% para el vendedor que pasa al cliente y deportiva 50. Validar si 50% es agencia o guia.'
    );
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM dbo.ControlTaxiComisiones
    WHERE BranchCode = N'CV'
      AND Proveedor = N'VENTAS ENTRE TIENDAS'
      AND TipoServicio = N'INTERDEPARTAMENTAL'
      AND ConTarjeta = 0
      AND VentaMinima = 0
      AND VentaMaxima IS NULL
)
BEGIN
    INSERT INTO dbo.ControlTaxiComisiones
    (
        BranchCode, Proveedor, TipoServicio, ConTarjeta, VentaMinima, VentaMaxima,
        ComisionAgencia, ComisionTaxista, ComisionVendedor, ComisionDeportiva,
        AplicaDegustacion, AplicaGasto, Activo, ReglaNombre, OrigenExcel,
        RequiereValidacion, Observaciones
    )
    VALUES
    (
        N'CV', N'VENTAS ENTRE TIENDAS', N'INTERDEPARTAMENTAL', 0, 0, NULL,
        NULL, NULL, 0.2000, 50.0000,
        1, 1, 0, N'VENTAS ENTRE TIENDAS S/TARJETA', N'CASCO!I42:J50',
        1, N'La rama sin tarjeta no deja claro si conserva el 19% o solo el 50%. Pendiente validar con negocio.'
    );
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM dbo.ControlTaxiComisiones
    WHERE BranchCode = N'CV'
      AND Proveedor = N'META DIARIA'
      AND TipoServicio = N'JOYERIA'
      AND ConTarjeta = 0
      AND VentaMinima = 10000
      AND VentaMaxima IS NULL
)
BEGIN
    INSERT INTO dbo.ControlTaxiComisiones
    (
        BranchCode, Proveedor, TipoServicio, ConTarjeta, VentaMinima, VentaMaxima,
        ComisionAgencia, ComisionTaxista, ComisionVendedor, ComisionDeportiva,
        AplicaDegustacion, AplicaGasto, Activo, ReglaNombre, OrigenExcel,
        RequiereValidacion, Observaciones
    )
    VALUES
    (
        N'CV', N'META DIARIA', N'JOYERIA', 0, 10000, NULL,
        NULL, NULL, NULL, NULL,
        0, 0, 0, N'META DIARIA JOYERIA', N'CASCO!L19:M21',
        1, N'El Excel muestra meta diaria de joyeria en 10000, pero no especifica formula de comision asociada.'
    );
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM dbo.ControlTaxiComisiones
    WHERE BranchCode = N'CV'
      AND Proveedor = N'META DIARIA'
      AND TipoServicio = N'TEQ/ART'
      AND ConTarjeta = 0
      AND VentaMinima = 7000
      AND VentaMaxima IS NULL
)
BEGIN
    INSERT INTO dbo.ControlTaxiComisiones
    (
        BranchCode, Proveedor, TipoServicio, ConTarjeta, VentaMinima, VentaMaxima,
        ComisionAgencia, ComisionTaxista, ComisionVendedor, ComisionDeportiva,
        AplicaDegustacion, AplicaGasto, Activo, ReglaNombre, OrigenExcel,
        RequiereValidacion, Observaciones
    )
    VALUES
    (
        N'CV', N'META DIARIA', N'TEQ/ART', 0, 7000, NULL,
        NULL, NULL, NULL, NULL,
        0, 0, 0, N'META DIARIA TEQ/ART', N'CASCO!L19:M21',
        1, N'El Excel muestra meta diaria de TEQ/ART en 7000, pero no especifica formula de comision asociada.'
    );
END;
GO
