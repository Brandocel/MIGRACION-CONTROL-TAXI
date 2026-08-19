namespace ControlTaxiWeb.Proposals.CascoBadgeSync;

public static class CascoBadgeSyncEndpointTests
{
    // Propuesta de casos de prueba, sin integrar al pipeline actual.

    public static readonly string[] Cases =
    {
        "1. payload valido con status R -> 200, inserted = 1",
        "2. mismo payload dos veces -> segundo 200, unchanged = 1",
        "3. status invalido X -> 422",
        "4. branchCode 28 -> 400",
        "5. branchCode vacio -> 400",
        "6. badgeId faltante -> 422",
        "7. lote de 2 gafetes validos -> 200, inserted = 2",
        "8. payload con duplicados badgeId+cycle -> 422",
        "9. content-type no json -> 415",
        "10. token invalido -> 401"
    };

    public static string SampleValidRequest => """
    {
      "branchCode": "CV",
      "gafetes": [
        {
          "badgeId": "2121",
          "barcode": "2121",
          "status": "R",
          "cycle": 1,
          "taxistaId": null,
          "taxistaName": "",
          "createdAt": "2026-07-18T11:31:18"
        }
      ]
    }
    """;

    public static string SampleSuccessResponse => """
    {
      "success": true,
      "branchCode": "CV",
      "received": 1,
      "inserted": 1,
      "updated": 0,
      "unchanged": 0,
      "errors": []
    }
    """;
}
