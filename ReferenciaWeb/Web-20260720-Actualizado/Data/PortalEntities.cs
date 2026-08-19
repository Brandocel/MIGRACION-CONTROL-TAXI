using Microsoft.EntityFrameworkCore;

namespace ControlTaxiWeb.Data;

public enum PortalDatabase
{
    CompuadmoPlaza,
    JoyeriaPlaza
}

public sealed class CompuadmoPlazaContext(DbContextOptions<CompuadmoPlazaContext> options)
    : DbContext(options)
{
    public DbSet<CompuadmoRemisionHeader> Remisiones => Set<CompuadmoRemisionHeader>();
    public DbSet<VendorRow> Vendors => Set<VendorRow>();
    public DbSet<ProductRow> Products => Set<ProductRow>();
    public DbSet<SalesCommissionRow> SalesCommissions => Set<SalesCommissionRow>();
    public DbSet<CashExpenseRow> CashExpenses => Set<CashExpenseRow>();
    public DbSet<TransportRow> Transports => Set<TransportRow>();
    public DbSet<GuideRow> Guides => Set<GuideRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CompuadmoRemisionHeader>(entity =>
        {
            entity.ToTable("remisioM");
            entity.HasKey(x => x.FolioRemision);
            entity.Property(x => x.FolioRemision).HasColumnName("folio_remision").HasMaxLength(20);
            entity.Property(x => x.FolioFactura).HasColumnName("folio_factura").HasMaxLength(20);
            entity.Property(x => x.Fecha).HasColumnName("fecha");
            entity.Property(x => x.TipoF).HasColumnName("tipof").HasMaxLength(10);
            entity.Property(x => x.Cliente).HasColumnName("cliente").HasMaxLength(6);
            entity.Property(x => x.Vendedor).HasColumnName("vendedor").HasMaxLength(4);
            entity.Property(x => x.Estatus).HasColumnName("estatus").HasMaxLength(1);
            entity.Property(x => x.Subtotal).HasColumnName("stotal");
            entity.Property(x => x.Iva).HasColumnName("iva");
            entity.Property(x => x.Total).HasColumnName("total");
            entity.Property(x => x.Saldo).HasColumnName("saldo");
            entity.Property(x => x.FechaCobro).HasColumnName("fecha_cobro");
            entity.Property(x => x.Observaciones).HasColumnName("observaciones");
            entity.Property(x => x.Descuento).HasColumnName("descuento");
            entity.Property(x => x.Tipo).HasColumnName("tipo");
            entity.Property(x => x.TipoCambio).HasColumnName("tipo_cambio");
            entity.Property(x => x.Letras).HasColumnName("letras").HasMaxLength(100);
            entity.Property(x => x.Usuario).HasColumnName("usuario").HasMaxLength(4);
            entity.Property(x => x.Hora).HasColumnName("hora").HasMaxLength(20);
            entity.Property(x => x.Almacen).HasColumnName("almacen").HasMaxLength(4);
            entity.Property(x => x.Moneda).HasColumnName("moneda").HasMaxLength(1);
            entity.Property(x => x.OperacionServicio).HasColumnName("oservicio").HasMaxLength(50);
            entity.Property(x => x.Efectivo).HasColumnName("efectivo");
            entity.Property(x => x.Tarjeta).HasColumnName("tarjeta");
            entity.Property(x => x.Dolares).HasColumnName("dolares");
            entity.Property(x => x.CotizacionDolar).HasColumnName("cotizadolar");
            entity.Property(x => x.FolioOperacion).HasColumnName("folio_operacion");
            entity.Property(x => x.Guia).HasColumnName("guia");
            entity.Property(x => x.CodigoGuia).HasColumnName("codigoguia").HasMaxLength(4);
            entity.Property(x => x.FolioRegistro).HasColumnName("folioregistro");
            entity.Property(x => x.Condiciones).HasColumnName("condiciones").HasMaxLength(50);
        });

        ConfigureSharedModel(modelBuilder);
    }

    internal static void ConfigureSharedModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<VendorRow>(entity =>
        {
            entity.ToTable("vendedor");
            entity.HasNoKey();
            entity.Property(x => x.VendorKey).HasColumnName("Vendedor");
            entity.Property(x => x.Name).HasColumnName("Nombre");
            entity.Property(x => x.PhoneNumber).HasColumnName("Telefono");
            entity.Property(x => x.CommissionPercent).HasColumnName("Porc_Comis");
        });

        modelBuilder.Entity<ProductRow>(entity =>
        {
            entity.ToTable("Productos");
            entity.HasNoKey();
            entity.Property(x => x.ProductId).HasColumnName("Producto");
            entity.Property(x => x.Barcode).HasColumnName("codigobarra");
            entity.Property(x => x.Name).HasColumnName("Nombre");
            entity.Property(x => x.Department).HasColumnName("depor");
            entity.Property(x => x.Price1).HasColumnName("precio1");
            entity.Property(x => x.PublicPrice).HasColumnName("preciopub");
            entity.Property(x => x.Iva).HasColumnName("iva");
            entity.Property(x => x.Currency).HasColumnName("moneda");
            entity.Property(x => x.Active).HasColumnName("activo");
        });

        modelBuilder.Entity<SalesCommissionRow>(entity =>
        {
            entity.ToTable("ventascom");
            entity.HasNoKey();
            entity.Property(x => x.Folio).HasColumnName("folio_remision");
            entity.Property(x => x.SaleDate).HasColumnName("fecha");
            entity.Property(x => x.BeneficiaryName).HasColumnName("nombre");
            entity.Property(x => x.SellerName).HasColumnName("vendedor");
            entity.Property(x => x.FixedAmount).HasColumnName("sumafijo");
            entity.Property(x => x.SportAmount).HasColumnName("sumadepor");
            entity.Property(x => x.FixedCommission).HasColumnName("comisionfijo");
            entity.Property(x => x.SportCommission).HasColumnName("comisiondepor");
        });

        modelBuilder.Entity<CashExpenseRow>(entity =>
        {
            entity.ToTable("caja");
            entity.HasNoKey();
            entity.Property(x => x.ExpenseDate).HasColumnName("fecha");
            entity.Property(x => x.TotalExpense).HasColumnName("egresototal");
        });

        modelBuilder.Entity<TransportRow>(entity =>
        {
            entity.ToTable("catrans");
            entity.HasNoKey();
            entity.Property(x => x.Code).HasColumnName("codigotrans");
            entity.Property(x => x.Name).HasColumnName("nombretransporte");
        });

        modelBuilder.Entity<GuideRow>(entity =>
        {
            entity.ToTable("guias");
            entity.HasNoKey();
            entity.Property(x => x.GuideKey).HasColumnName("guia");
            entity.Property(x => x.Name).HasColumnName("nombre");
            entity.Property(x => x.Company).HasColumnName("empresa");
            entity.Property(x => x.PhoneNumber).HasColumnName("telefono");
        });
    }
}

public sealed class JoyeriaPlazaContext(DbContextOptions<JoyeriaPlazaContext> options)
    : DbContext(options)
{
    public DbSet<JoyeriaRemisionHeader> Remisiones => Set<JoyeriaRemisionHeader>();
    public DbSet<VendorRow> Vendors => Set<VendorRow>();
    public DbSet<ProductRow> Products => Set<ProductRow>();
    public DbSet<SalesCommissionRow> SalesCommissions => Set<SalesCommissionRow>();
    public DbSet<CashExpenseRow> CashExpenses => Set<CashExpenseRow>();
    public DbSet<TransportRow> Transports => Set<TransportRow>();
    public DbSet<GuideRow> Guides => Set<GuideRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<JoyeriaRemisionHeader>(entity =>
        {
            entity.ToTable("remisioM");
            entity.HasKey(x => x.FolioFactura);
            entity.Property(x => x.FolioPedido).HasColumnName("folio_pedido").HasMaxLength(20);
            entity.Property(x => x.FolioFactura).HasColumnName("folio_factura").HasMaxLength(20);
            entity.Property(x => x.Fecha).HasColumnName("fecha");
            entity.Property(x => x.Tipo).HasColumnName("tipo").HasMaxLength(10);
            entity.Property(x => x.Cliente).HasColumnName("cliente").HasMaxLength(6);
            entity.Property(x => x.Vendedor).HasColumnName("vendedor");
            entity.Property(x => x.Estatus).HasColumnName("estatus").HasMaxLength(1);
            entity.Property(x => x.Subtotal).HasColumnName("stotal");
            entity.Property(x => x.Iva).HasColumnName("iva");
            entity.Property(x => x.Total).HasColumnName("total");
            entity.Property(x => x.Saldo).HasColumnName("saldo");
            entity.Property(x => x.FechaCobro).HasColumnName("fecha_cobro");
            entity.Property(x => x.Observaciones).HasColumnName("observaciones");
            entity.Property(x => x.Descuento).HasColumnName("descuento");
            entity.Property(x => x.Moneda).HasColumnName("moneda");
            entity.Property(x => x.TipoCambio).HasColumnName("tipo_cambio");
            entity.Property(x => x.Letras).HasColumnName("letras").HasMaxLength(100);
            entity.Property(x => x.Cajero).HasColumnName("cajero");
            entity.Property(x => x.Procesado).HasColumnName("procesado").HasMaxLength(1);
            entity.Property(x => x.NCorte).HasColumnName("ncorte");
            entity.Property(x => x.Hora).HasColumnName("hora");
            entity.Property(x => x.Comisionista).HasColumnName("comisionista").HasMaxLength(4);
            entity.Property(x => x.Peso).HasColumnName("peso");
            entity.Property(x => x.CostoTienda).HasColumnName("costotienda");
            entity.Property(x => x.CuantosVend).HasColumnName("cuantosvend");
            entity.Property(x => x.UtilidadBruta).HasColumnName("utilidadbruta");
            entity.Property(x => x.Almacen).HasColumnName("almacen").HasMaxLength(4);
            entity.Property(x => x.Usuario).HasColumnName("usuario").HasMaxLength(4);
            entity.Property(x => x.FolioOperacion).HasColumnName("folio_operacion");
            entity.Property(x => x.FolioRegistro).HasColumnName("folio_registro");
        });

        CompuadmoPlazaContext.ConfigureSharedModel(modelBuilder);
    }
}

public sealed class CompuadmoRemisionHeader
{
    public string FolioRemision { get; set; } = string.Empty;
    public string? FolioFactura { get; set; }
    public DateTime? Fecha { get; set; }
    public string? TipoF { get; set; }
    public string? Cliente { get; set; }
    public string? Vendedor { get; set; }
    public string? Estatus { get; set; }
    public double? Subtotal { get; set; }
    public double? Iva { get; set; }
    public double? Total { get; set; }
    public double? Saldo { get; set; }
    public DateTime? FechaCobro { get; set; }
    public string? Observaciones { get; set; }
    public float? Descuento { get; set; }
    public int? Tipo { get; set; }
    public float? TipoCambio { get; set; }
    public string? Letras { get; set; }
    public string? Usuario { get; set; }
    public string? Hora { get; set; }
    public string? Almacen { get; set; }
    public string? Moneda { get; set; }
    public string? OperacionServicio { get; set; }
    public float? Efectivo { get; set; }
    public float? Tarjeta { get; set; }
    public float? Dolares { get; set; }
    public float? CotizacionDolar { get; set; }
    public int? FolioOperacion { get; set; }
    public int? Guia { get; set; }
    public string? CodigoGuia { get; set; }
    public long? FolioRegistro { get; set; }
    public string? Condiciones { get; set; }
}

public sealed class JoyeriaRemisionHeader
{
    public string FolioFactura { get; set; } = string.Empty;
    public string? FolioPedido { get; set; }
    public DateTime? Fecha { get; set; }
    public string? Tipo { get; set; }
    public string? Cliente { get; set; }
    public int? Vendedor { get; set; }
    public string? Estatus { get; set; }
    public double? Subtotal { get; set; }
    public double? Iva { get; set; }
    public double? Total { get; set; }
    public double? Saldo { get; set; }
    public DateTime? FechaCobro { get; set; }
    public string? Observaciones { get; set; }
    public float? Descuento { get; set; }
    public int? Moneda { get; set; }
    public float? TipoCambio { get; set; }
    public string? Letras { get; set; }
    public int? Cajero { get; set; }
    public string? Procesado { get; set; }
    public int? NCorte { get; set; }
    public DateTime? Hora { get; set; }
    public string? Comisionista { get; set; }
    public float? Peso { get; set; }
    public float? CostoTienda { get; set; }
    public int? CuantosVend { get; set; }
    public float? UtilidadBruta { get; set; }
    public string? Almacen { get; set; }
    public string? Usuario { get; set; }
    public int? FolioOperacion { get; set; }
    public long? FolioRegistro { get; set; }
}

public sealed class VendorRow
{
    public string? VendorKey { get; set; }
    public string? Name { get; set; }
    public string? PhoneNumber { get; set; }
    public float? CommissionPercent { get; set; }
}

public sealed class ProductRow
{
    public int ProductId { get; set; }
    public string? Barcode { get; set; }
    public string? Name { get; set; }
    public string? Department { get; set; }
    public float? Price1 { get; set; }
    public float? PublicPrice { get; set; }
    public float? Iva { get; set; }
    public string? Currency { get; set; }
    public string? Active { get; set; }
}

public sealed class SalesCommissionRow
{
    public string? Folio { get; set; }
    public DateTime? SaleDate { get; set; }
    public string? BeneficiaryName { get; set; }
    public string? SellerName { get; set; }
    public float? FixedAmount { get; set; }
    public float? SportAmount { get; set; }
    public float? FixedCommission { get; set; }
    public float? SportCommission { get; set; }
}

public sealed class CashExpenseRow
{
    public DateTime? ExpenseDate { get; set; }
    public float? TotalExpense { get; set; }
}

public sealed class TransportRow
{
    public string? Code { get; set; }
    public string? Name { get; set; }
}

public sealed class GuideRow
{
    public string? GuideKey { get; set; }
    public string? Name { get; set; }
    public string? Company { get; set; }
    public string? PhoneNumber { get; set; }
}
