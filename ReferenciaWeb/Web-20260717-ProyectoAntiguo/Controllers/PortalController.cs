using ControlTaxiWeb.Data;
using ControlTaxiWeb.Models;
using ControlTaxiWeb.Services;
using Microsoft.AspNetCore.Mvc;

namespace ControlTaxiWeb.Controllers;

public sealed class PortalController(PortalService service) : Controller
{
    public async Task<IActionResult> Dashboard(
        PortalDatabase database = PortalDatabase.CompuadmoPlaza,
        DateTime? date = null,
        CancellationToken cancellationToken = default)
    {
        var model = await service.GetDashboardAsync(database, date, cancellationToken);
        return View(model);
    }

    public async Task<IActionResult> Operations(
        PortalDatabase database = PortalDatabase.CompuadmoPlaza,
        DateTime? date = null,
        string? query = null,
        CancellationToken cancellationToken = default)
    {
        var model = await service.GetOperationsAsync(database, date, query, cancellationToken);
        return View(model);
    }

    public async Task<IActionResult> Commissions(
        PortalDatabase database = PortalDatabase.CompuadmoPlaza,
        string? query = null,
        CancellationToken cancellationToken = default)
    {
        var model = await service.GetCommissionsAsync(database, query, cancellationToken);
        return View(model);
    }

    public async Task<IActionResult> Vendors(
        PortalDatabase database = PortalDatabase.CompuadmoPlaza,
        string? query = null,
        CancellationToken cancellationToken = default)
    {
        var model = await service.GetVendorsAsync(database, query, cancellationToken);
        return View(model);
    }

    public async Task<IActionResult> Products(
        PortalDatabase database = PortalDatabase.CompuadmoPlaza,
        string? query = null,
        CancellationToken cancellationToken = default)
    {
        var model = await service.GetProductsAsync(database, query, cancellationToken);
        return View(model);
    }

    public async Task<IActionResult> Catalogs(
        PortalDatabase database = PortalDatabase.CompuadmoPlaza,
        CancellationToken cancellationToken = default)
    {
        var model = await service.GetCatalogsAsync(database, cancellationToken);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> CreateOperation(
        PortalDatabase database = PortalDatabase.CompuadmoPlaza,
        string? success = null,
        CancellationToken cancellationToken = default)
    {
        var model = await service.GetCreateOperationAsync(database, cancellationToken, success);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateOperation(
        CreateOperationInputModel input,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            var invalidModel = await service.GetCreateOperationAsync(input.Database, cancellationToken, input: input);
            return View(invalidModel);
        }

        var folio = await service.CreateOperationAsync(input, cancellationToken);
        return RedirectToAction(nameof(CreateOperation), new
        {
            database = input.Database,
            success = $"Operacion guardada correctamente con folio {folio}."
        });
    }

    public IActionResult Error() => View();
}
