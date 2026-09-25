using System;
using ControlTaxiDesktop.DevChecks;

class Program
{
    static int Main()
    {
        try
        {
            var result = RuntimeConfigReader.Read();
            Console.WriteLine(result.LoadedConfigPath ?? "(none)");
            Console.WriteLine(result.IsLocalOverride ? "true" : "false");
            Console.WriteLine(result.CvServer);
            Console.WriteLine(result.CvDb);
            Console.WriteLine(result.P28Server);
            Console.WriteLine(result.P28Db);
            Console.WriteLine(result.CvHasCredentials ? "yes" : "no");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 2;
        }
    }
}
