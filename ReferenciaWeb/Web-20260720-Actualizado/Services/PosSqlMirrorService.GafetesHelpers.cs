using ControlTaxiWeb.Data;
using Microsoft.EntityFrameworkCore;

namespace ControlTaxiWeb.Services
{
    public partial class PosSqlMirrorService
    {
        private async Task<int> EnsurePosGafeteAsync(
            System.Data.Common.DbConnection connection,
            System.Data.Common.DbTransaction transaction,
            string numero)
        {
            var existing = await _appContext.Gafetes
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Numero == numero);
            if (existing != null)
                return existing.Id;

            var gafete = new AppGafete
            {
                Numero = numero,
                Estatus = "Disponible"
            };
            await _appContext.Gafetes.AddAsync(gafete);
            await _appContext.SaveChangesAsync();
            return gafete.Id;
        }

        private async Task<bool> HasActiveGafeteAssignmentAsync(
            System.Data.Common.DbConnection connection,
            System.Data.Common.DbTransaction transaction,
            int gafeteId) =>
            await _appContext.GafeteAsignaciones
                .AsNoTracking()
                .AnyAsync(x => x.GafeteId == gafeteId
                    && x.Estatus == "Asignado"
                    && x.FechaRegreso == null);
    }
}
