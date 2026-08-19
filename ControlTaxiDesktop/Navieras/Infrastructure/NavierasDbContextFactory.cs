using Microsoft.EntityFrameworkCore;
using System.IO;

namespace ControlTaxiDesktop.Navieras.Infrastructure;

public static class NavierasDbContextFactory
{
    public static NavierasDbContext Create()
    {
        var dataDirectory = Path.Combine(AppContext.BaseDirectory, "DatosLocal");
        Directory.CreateDirectory(dataDirectory);
        var path = Path.Combine(dataDirectory, "NavierasModule.db");
        var options = new DbContextOptionsBuilder<NavierasDbContext>()
            .UseSqlite($"Data Source={path}")
            .Options;
        var context = new NavierasDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }
}
