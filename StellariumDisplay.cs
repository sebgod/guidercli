namespace PHD2
{
    internal class StellariumDisplay : IDisposable
    {
        private readonly IUdpReceiver _receiver;
        private readonly HttpClient _stellariumClient;
        private readonly int _telescope;

        public StellariumDisplay(Uri stellariumAddress, int telescope)
        : this(new UdpReceiver(), new HttpClient { BaseAddress = stellariumAddress }, telescope) {
            // calls below
        }

        public StellariumDisplay(IUdpReceiver receiver, HttpClient stellariumClient, int telescope) {
            _receiver = receiver;
            _stellariumClient = stellariumClient;
            _telescope = telescope;
        }

        private bool disposedValue;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _receiver.Dispose();
                    _stellariumClient.Dispose();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        public async Task LoopAsync(CancellationToken token = default)
        {
            while (!token.IsCancellationRequested) {
                if (await _receiver.ReceiveAsync<UpdateMessage>(token) is UpdateMessage updateMessage) {
                    Console.WriteLine("Update telescope {0} RA = {1} DEC = {2} via {3}",
                        _telescope, updateMessage.RA, updateMessage.DEC, _stellariumClient.BaseAddress);

                    if (!await UpdateStellariumViewAndTelescopeAsync(updateMessage)) {
                        break;
                    }
                }
            }
        }

        async Task<bool> UpdateStellariumViewAndTelescopeAsync(UpdateMessage updateMessage)
        {
            const double degToRad = Math.PI / 180.0; 
    
            try {    
                var raRad = degToRad * updateMessage.RA;
                var decRad = degToRad * updateMessage.DEC;
                var sinRA = Math.Sin(raRad);
                var cosRA = Math.Cos(raRad);
                var sinDec = Math.Sin(decRad);
                var cosDec = Math.Cos(decRad);

                var x = cosDec * cosRA;
                var y = cosDec * sinRA;
                var z = sinDec;
                var viewBody = new []{ KeyValuePair.Create("j2000", $"[{x},{y},{z}]") };
                using var viewRequestContent = new FormUrlEncodedContent(viewBody);
                var viewResponse = await _stellariumClient.PostAsync("api/main/view", viewRequestContent);
                if (viewResponse.IsSuccessStatusCode) {
                    if (_telescope >= 1) {
                        var slewTelescopeBody = new []{ KeyValuePair.Create("id", $"actionSlew_Telescope_To_Direction_{_telescope}") };
                        using var slewTelescopeRequestContent = new FormUrlEncodedContent(slewTelescopeBody);
                        var slewResponse = await _stellariumClient.PostAsync("api/stelaction/do", slewTelescopeRequestContent);
                        if (slewResponse.IsSuccessStatusCode) {
                            return true;
                        } else {
                            Console.Error.WriteLine("Failed slewing {0}", await slewResponse.Content.ReadAsStringAsync());
                            return false;
                        }
                    } else {
                        return true;
                    }
                } else {
                    Console.Error.WriteLine("Failed setting view {0}", await viewResponse.Content.ReadAsStringAsync());
                    return false;
                }
            } catch (Exception ex) {
                Console.Error.WriteLine("Error {0} while slewing", ex.Message);
                return false;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~StellariumDisplay()
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
}