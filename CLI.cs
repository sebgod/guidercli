using System.Runtime.InteropServices;

namespace PHD2;

class Program
{
    static async Task<int> Main(string[] args)
    {
        string command = args.Length == 0 ? "solve" : args[0];

        switch (command) {
            case "solve":
                var host = args.Length > 1 && !string.IsNullOrWhiteSpace(args[1]) ? args[1] : "localhost";
                var instance = args.Length > 2 && uint.TryParse(args[2], out var parsedArg2) ? parsedArg2 : 1;

                using (var plateSolve = new PlateSolve(host, instance)) {
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