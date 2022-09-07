using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PHD2;

public class PlateSolve : IDisposable
{    
    const string MainProfile = "FinderScope";
    const string SimProfile = "Simulator";

    static readonly string[] ProfilesToTest = new [] {
        MainProfile,
        SimProfile
    };

    private readonly IGuider _guider;
    private readonly IUdpSender _sender;
    private readonly string _astapCli;
    private bool disposedValue;

    public PlateSolve(string host, uint instance, string astapCli)
        : this(new GuiderImpl(host, instance), new UdpSender(), astapCli)
    {
        // calls below
    }

    public PlateSolve(IGuider guider, IUdpSender sender, string astapCli)
    {
        _guider = guider;
        _sender = sender;
        _astapCli = astapCli;
    }

    public async Task<int> LoopAsync()
    {
        _guider.Connect();
        var profileToConnect = FindPreferredProfile(ProfilesToTest);

        if (profileToConnect is null)
        {
            Console.Error.WriteLine("Could not find any of {0}", string.Join(", ", ProfilesToTest));
            return -1;
        }

        _guider.ConnectEquipment(profileToConnect);
        _guider.UnhandledEvent += (sender, eventArgs) =>
        {
            Console.Error.WriteLine("Unhandled event: {0}: {1}", eventArgs.Event, eventArgs.Payload);
        };

        _guider.Loop();

        double? lastRA = null;
        double? lastDec = null;
        var expTime = TimeSpan.FromSeconds(1.5);

        while (_guider.IsConnected && _guider.IsLooping())
        {
            var filePrefix = _guider.SaveImage();
            Console.WriteLine("Saved image: {0}", filePrefix);

            var sw = Stopwatch.StartNew();
            if (filePrefix is not null)
            {
                (lastRA, lastDec) = await ProcessFileAsync(filePrefix, lastRA, lastDec);

                if (lastRA.HasValue && lastDec.HasValue)
                {
                    await _sender.BroadcastAsJsonUtf8Async(new UpdateMessage(lastRA.Value, lastDec.Value));
                }
            }
            var processTime = sw.Elapsed;
            sw.Stop();

            // wait the difference (if positive)
            if (processTime < expTime)
            {
                await Task.Delay((expTime - processTime) + TimeSpan.FromMilliseconds(100.0));
            }
        }

        return 0;
    }

    
    string? FindPreferredProfile(params string[] profilesToTest)
    {
        var profiles = _guider.GetEquipmentProfiles();
        string? profileToConnect = null;
        foreach (var profileToTest in profilesToTest)
        {
            if (profiles.Contains(profileToTest))
            {
                profileToConnect = profileToTest;
                break;
            }
        }

        return profileToConnect;
    }

    static readonly Regex IniKVPattern = new Regex(@"^\s*(\w+)\s*=\s*([^#]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    async Task<(double? ra, double? dec)> ProcessFileAsync(string filePrefix, double? lastRA, double? lastDec)
    {
        double? newRA = null;
        double? newDec = null;
        string fitsFile = filePrefix + ".fits";
        string iniFile = filePrefix + ".ini";
        string wcsFile = filePrefix + ".wcs";
        File.Move(filePrefix, fitsFile);

        var argsBuilder = new StringBuilder()
            .AppendFormat("-f \"{0}\"", fitsFile);
        
        if (lastRA.HasValue && lastDec.HasValue) {
            argsBuilder
                .AppendFormat(" -ra {0:0.0}", Math.Clamp(lastRA.Value / 15.0, 0, 24))
                .AppendFormat(" -spd {0:0.0}", Math.Clamp(lastDec.Value + 90.0, 0.0, 180.0))
                .AppendFormat(" -r {0}", 5);
        }

        var args = argsBuilder.ToString();
        Console.WriteLine("Run {0} {1} last RA={2:0.000} DEC={3:0.000}", _astapCli, args, lastRA, lastDec);

        var info = new ProcessStartInfo(_astapCli, args)
        {
            CreateNoWindow = false,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        var proc = Process.Start(info);
        var stdout = new ConcurrentQueue<string>();
        var stderr = new ConcurrentQueue<string>();
        if (proc is not null)
        {
            proc.OutputDataReceived += (sender, eventArgs) =>
            {
                if (eventArgs.Data is string data)
                {
                    stdout.Append(data);
                }
            };
            proc.ErrorDataReceived += (sender, eventArgs) =>
            {
                if (eventArgs.Data is string data)
                {
                    stderr.Append(data);
                }
            };
            proc.BeginErrorReadLine();
            proc.BeginOutputReadLine();
            await proc.WaitForExitAsync();

            foreach (var line in stdout)
            {
                Console.WriteLine(line);
            }
            foreach (var line in stderr)
            {
                Console.Error.WriteLine(line);
            }
        }

        File.Delete(fitsFile);
        var hasIni = File.Exists(iniFile);
        var hasWcs = File.Exists(wcsFile);

        if (hasIni)
        {
            var kvs = new Dictionary<string, string>(
                from line in await File.ReadAllLinesAsync(iniFile)
                where !string.IsNullOrWhiteSpace(line)
                let match = IniKVPattern.Match(line)
                where match.Success
                let key = match.Groups[1].Value
                let value = match.Groups[2].Value.Trim()
                select KeyValuePair.Create(key, value)
            );

            if (kvs.TryGetValue("PLTSOLVD", out var isPlateSolved) && isPlateSolved == "T") {
                newRA = ParseDouble(kvs, "CRVAL1");
                newDec = ParseDouble(kvs, "CRVAL2");
            }
    
            File.Delete(iniFile);
        }
        if (hasWcs)
        {
            File.Delete(wcsFile);
        }

        return (newRA, newDec);
    }

    static double? ParseDouble(Dictionary<string, string> dict, string key) {
        if (dict.TryGetValue(key, out var value) && double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var @float)) {
            return @float;
        } else {
            return null;
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                _guider.Dispose();
                _sender.Dispose();
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~PlateSolve()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}