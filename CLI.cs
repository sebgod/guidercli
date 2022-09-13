using System.Runtime.InteropServices;

namespace PHD2;

class Program {
    static string? DefaultAstapCliLocation {
        get {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "astap", "astap_cli.exe");
            } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
                return "astap";
            }
            return null;
        }
    }

    static async Task<int> Main(string[] args)
    {
        if (args.Length == 0) {
            Console.Error.WriteLine("Need to provide arguments!");
            return -1;
        }

        switch (args[0]) {
            case "solve":
                var host = args.Length > 1 && !string.IsNullOrWhiteSpace(args[1]) ? args[1] : "localhost";
                var instance = args.Length > 2 && uint.TryParse(args[2], out var parsedArg2) ? parsedArg2 : 1;
                var astapCli = DefaultAstapCliLocation;

                if (astapCli is null || !File.Exists(astapCli))
                {
                    Console.Error.WriteLine("Unusable ASTAP at {0}", astapCli);
                    return -2;
                }

                using (var plateSolve = new PlateSolve(host, instance, astapCli)) {
                    return await plateSolve.LoopAsync();
                }

            case "display":
                int telescope = -1;
                Uri? address = null;
                bool hasCustomTelescope = false;
                bool hasCustomAddress = false;

                for (var idx = 1; idx < args.Length; idx++) {
                    hasCustomTelescope = hasCustomTelescope || int.TryParse(args[idx], out telescope);
                    hasCustomAddress = hasCustomAddress || Uri.TryCreate(args[idx], UriKind.Absolute, out address);
                }

                using (var stellariumDisplay = new StellariumDisplay(address ?? new Uri("http://localhost:8090"), telescope)) {
                    await stellariumDisplay.LoopAsync();
                }
                return 0;

            default:
                Console.Error.WriteLine("Unrecognised command: {0}", args[0]);
                return -3;
        }
    }
}