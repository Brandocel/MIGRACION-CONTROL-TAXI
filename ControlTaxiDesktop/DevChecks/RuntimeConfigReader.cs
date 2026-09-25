using System;
using ControlTaxiDesktop.Services;

namespace ControlTaxiDesktop.DevChecks;

public static class RuntimeConfigReader
{
    // Returns: (loadedConfigPath, isLocalOverride, cvServer, cvDb, p28Server, p28Db, cvHasCredentials)
    public static (string? LoadedConfigPath, bool IsLocalOverride, string CvServer, string CvDb, string P28Server, string P28Db, bool CvHasCredentials) Read()
    {
        var branches = new BranchConfigurationService();
        var loaded = branches.GetLoadedConfigPath();
        var isLocal = branches.IsLocalOverrideActive();
        var repo = new LocalUserRepository(new LocalDatabase(true));
        var cv = repo.GetBranchConnectionInfo("CV");
        var p28 = repo.GetBranchConnectionInfo("P28");

        // Check if CV has credentials available (password present in environment or credential store)
        var cvHasCred = false;
        try
        {
            // Do not apply credential stores that change server/db; only detect presence of password
            if (CascoCredentialStore.HasSavedCredential())
                cvHasCred = true;
            else if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CASCO_SQL_PASSWORD")))
                cvHasCred = true;
        }
        catch
        {
            cvHasCred = false;
        }

        return (loaded, isLocal, cv.DataSource ?? string.Empty, cv.InitialCatalog ?? string.Empty, p28.DataSource ?? string.Empty, p28.InitialCatalog ?? string.Empty, cvHasCred);
    }
}
