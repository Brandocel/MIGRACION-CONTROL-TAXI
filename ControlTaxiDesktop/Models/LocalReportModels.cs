namespace ControlTaxiDesktop.Models;

public sealed record LocalTaxiReportRow(string DriverName, string Code, string Type, DateTime Date, int AdultPassengers, int ChildPassengers, int InfantPassengers, string Hotel, decimal GrossSale, decimal Cash, decimal Card, decimal Amex, decimal Payout, decimal Expenses, decimal NetSale, decimal Commission, string PaymentDate, string Status);
public sealed record LocalSpecialReportRow(string Date, string FolioControl, string AppFolio, string OperationFolio, string PosFolio, string Driver, string Badge, string TransportType, string Hotel, int Passengers, decimal Sale, decimal Payout, decimal PayoutPaid, decimal Commission, decimal Paid, string Status);
public sealed record LocalCommissionBrowserRow(
    string Folio,
    string SaleFolio,
    DateTime Fecha,
    string Unidad,
    string NumeroUnidad,
    string Nombre,
    int Pax,
    string Hotel,
    decimal VentaArtesania,
    decimal VentaFarmacia,
    decimal VentaTienda,
    decimal VentaJoyeria,
    decimal VentaTotal,
    string Ticket,
    string FormaPago,
    decimal DescuentoPorcentaje,
    decimal Dejada,
    decimal BebidasCajasRegalo,
    decimal Reparacion,
    decimal Degustacion,
    decimal PorcentajeComision,
    decimal PagoComision,
    decimal Pagado,
    decimal Saldo,
    string Estatus,
    string Vendedor,
    string Gafete,
    // Estatus del pago de la DEJADA al taxista. Es un pago distinto al de la comision, por eso
    // va en su propia columna: una comision puede estar pagada y la dejada no, o al reves.
    string EstatusDejada = "",
    // Gastos varios ya descontados de la base. Va aparte de Degustacion/Reparacion porque son
    // conceptos distintos y, si no se muestra, la comision no cuadra con los importes visibles.
    decimal GastosVarios = 0m,
    // Ticket del folio al que se le cargo la dejada completa. En los demas tickets del folio la
    // dejada va en cero, y este campo es el que permite decir en pantalla donde quedo.
    string PayoutTicket = "",
    // Autorizacion de pago de comision (confirmado con el usuario 2026-08-24: el pago dejo de
    // ser libre). Vacio = no autorizado. Cualquier usuario con el permiso puede pagar una vez
    // autorizado, incluida la misma persona que autorizo.
    string AutorizadoEn = "",
    string AutorizadoPor = "",
    string PagadoPor = "")
{
    public bool EstaAutorizada => !string.IsNullOrWhiteSpace(AutorizadoEn);
    public bool PuedeAutorizar => PagoComision > 0m && !EstaAutorizada && !string.Equals(Estatus, "PAGADA", StringComparison.OrdinalIgnoreCase);
    public bool PuedePagar => PagoComision > 0m && Saldo > 0m && EstaAutorizada && !string.Equals(Estatus, "PAGADA", StringComparison.OrdinalIgnoreCase);
    public bool PuedeImprimirTicket => PagoComision > 0m && string.Equals(Estatus, "PAGADA", StringComparison.OrdinalIgnoreCase);
}
public sealed record LocalCommissionDiagnosticRow(
    string Folio,
    string Ticket,
    DateTime Fecha,
    decimal Venta,
    string Gafete,
    string Unidad,
    string Taxista,
    string Hotel,
    string FormaPago,
    decimal Efectivo,
    decimal Tarjeta,
    decimal Amex,
    decimal RetencionTarjeta,
    decimal RetencionAmex,
    decimal Dejada,
    decimal Gastos,
    decimal PorcentajeUnidad,
    decimal BaseComision,
    decimal ComisionCalculada,
    string Fuente);
public sealed record LocalCommissionIntegrityIssue(
    string Folio,
    DateTime Fecha,
    string Categoria,
    string Severidad,
    string Descripcion,
    string Evidencia,
    string AccionSugerida);
public sealed record LocalSalesBrowserRow(
    string Folio,
    string Factura,
    DateTime Fecha,
    string Cliente,
    string Vendedor,
    string Taxista,
    string Usuario,
    string FolioRegistro,
    string FolioApp,
    string FolioControl,
    string Gafete,
    string Transporte,
    int Pax,
    decimal Subtotal,
    decimal Iva,
    decimal Total,
    decimal Efectivo,
    decimal Tarjeta,
    decimal Dolares,
    decimal TipoCambio,
    string OrigenVenta,
    decimal TotalPagos = 0m,
    decimal DiferenciaPago = 0m,
    string FormaPagoDetalle = "",
    string MonedaDetalle = "",
    string PagoDetalle = "",
    bool PagoInconsistente = false);
public sealed record LocalSalesTicketRow(
    int Cantidad,
    string Producto,
    string Departamento,
    decimal Precio,
    decimal Importe,
    string Referencia,
    string Factura,
    string FechaVenta,
    string GafetesAsignados,
    string OrigenVenta);
public sealed record LocalUserRow(string UserName, string Role, string Status, string CreatedAt, string Permissions, string BranchCode)
{
    public string BranchName => BranchCode?.Trim().ToUpperInvariant() switch
    {
        "CV" => "Casco Viejo",
        "ALL" => "Ambas",
        _ => "Plaza 28"
    };
}
public sealed record LocalBadgeMovement(string Number, string Staff, string OperationFolio, string AssignedAt, string Status, string ReturnedAt, string Unit, string Phone, string Nationality);
public sealed record LocalTicketLine(string Campo, string Valor);
public sealed record LocalCuadreResumenRow(string Concepto, int Pax, int Entraron, int Salieron, int Unidades, decimal Dejada, decimal Venta, decimal Comision, decimal Gastos, decimal Neto);
public sealed record LocalCuadreCorteRow(DateTime Fecha, decimal Efectivo, decimal Tarjeta, decimal Pagos, decimal Gastos, decimal Esperado, decimal Contado, decimal Diferencia, string Estatus);
public sealed record LocalOperationsPreviewRow(
    string Taxista,
    string Fecha,
    int Pax,
    string Hotel,
    decimal Dejada,
    decimal Importe,
    decimal Comision,
    decimal Pago,
    string FolioOriginal = "",
    string FolioLocal = "",
    string Gafete = "",
    string Estatus = "",
    string FechaPago = "",
    string UsuarioPago = "",
    string TicketPago = "",
    string Sitio = "",
    string Origen = "",
    string Destino = "",
    string Unidad = "",
    string Placas = "",
    string TipoServicio = "",
    string Notas = "",
    string Vendedor = "",
    int Adulto = 0,
    int Joven = 0,
    int Nino = 0);
public sealed record LocalCommissionPaymentPreviewRow(
    string Folio,
    string FechaPago,
    string Taxista,
    string Transporte,
    decimal Comision,
    decimal Pago,
    string Estatus,
    string Gafete = "",
    decimal Dejada = 0m,
    string UsuarioPago = "",
    string TicketPago = "",
    string Sitio = "",
    string FolioLocal = "",
    decimal Importe = 0m);
public sealed record LocalRegistroDiarioRow(
    string FolioOperacion,
    string FolioControl,
    string Fuente,
    string UsuarioOrigen,
    string Ticket,
    string Gafete,
    int CantidadTickets,
    string Hotel,
    string LlegadaSucursal,
    string FechaTexto,
    string HoraTexto,
    int Pax,
    string Taxista,
    string Nacionalidad,
    string TipoOperacion,
    string Origen,
    string Sitio,
    string Destino,
    string Unidad,
    string Placas,
    string Telefono,
    string Notas,
    decimal Total,
    decimal Efectivo,
    decimal Tarjeta);
