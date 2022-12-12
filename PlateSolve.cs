using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Astap.Lib.Astrometry.PlateSolve;
using Astap.Lib.Devices.Guider;

namespace PHD2;

public class PlateSolve : IDisposable
{
    const string MainProfile = "FinderScope";
    const string SimProfile = "Simulator";

    static readonly string[] ProfilesToTest = new[] {
        MainProfile,
        SimProfile
    };

    private readonly GuiderDevice _guiderDevice;
    private IGuider _guider;
    private readonly IUdpSender _sender;
    private readonly IPlateSolver _plateSolver;
    private bool disposedValue;

    public PlateSolve(string host, uint instance)
        : this(new GuiderDevice("PHD2", $"{host}/{instance}", ""))
    {
        // calls below
    }

    public PlateSolve(GuiderDevice guiderDevice)
        : this(
              guiderDevice,
              guiderDevice.TryInstantiate(out IGuider? driver)
                ? driver
                : throw new ArgumentException($"Cannot find guider at {guiderDevice}", nameof(guiderDevice)),
              new UdpSender(),
              new AstrometryNetPlateSolver())
    {
        // calls below
    }

    public PlateSolve(GuiderDevice device, IGuider guider, IUdpSender sender, IPlateSolver plateSolver)
    {
        _guiderDevice = device;
        _guider = guider;
        _sender = sender;
        _plateSolver = plateSolver;
    }

    public async Task<int> LoopAsync()
    {
        _guider.Connected = true;
        var profileToConnect = FindPreferredProfile(ProfilesToTest);

        if (profileToConnect is null)
        {
            Console.Error.WriteLine("Could not find any of {0}", string.Join(", ", ProfilesToTest));
            return -1;
        }

        if (new GuiderDevice(_guiderDevice.DeviceType, _guiderDevice.DeviceId, profileToConnect).TryInstantiate(out IGuider? guiderProfile))
        {
            _guider = guiderProfile;
            _guider.Connected = true;
            _guider.ConnectEquipment();
        }
        else
        {
            Console.Error.WriteLine("Cannot connect to {0}", profileToConnect);
            return -1;
        }

        _guider.UnhandledEvent += (sender, eventArgs) =>
        {
            Console.Error.WriteLine("Unhandled event: {0}: {1}", eventArgs.Event, eventArgs.Payload);
        };

        _guider.Loop();

        (double ra, double dec)? lastSolution = null;
        var expTime = TimeSpan.FromSeconds(1.5);
        var cameraFrameSize = _guider.CameraFrameSize();
        if (!cameraFrameSize.HasValue)
        {
            Console.Error.WriteLine("Unable to get camera frame size");
            return -1;
        }
        var imageDims = new ImageDim(_guider.PixelScale(), cameraFrameSize.Value.width, cameraFrameSize.Value.height);

        while (_guider.Connected && _guider.IsLooping())
        {
            var cts = new CancellationTokenSource(expTime * (lastSolution.HasValue ? 2 : 10));
            var filePrefix = _guider.SaveImage();

            var sw = Stopwatch.StartNew();
            if (filePrefix is not null)
            {
                var fitsFile = Path.ChangeExtension(filePrefix, $".{Random.Shared.Next()}.fits");
                File.Move(filePrefix, fitsFile);
                Console.WriteLine("Saved image: {0}", fitsFile);

                try
                {
                    lastSolution = await _plateSolver.SolveFileAsync(fitsFile, imageDims, 0.4f, searchRadius: 1, searchOrigin: lastSolution, cancellationToken: cts.Token);
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine("Error while solving {0}: {1}", fitsFile, e.Message);
                    lastSolution = null;
                    continue;
                }
                finally
                {
                    File.Delete(fitsFile);
                }

                if (lastSolution is (double ra, double dec))
                {
                    await _sender.BroadcastAsJsonUtf8Async(new UpdateMessage(ra, dec));
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