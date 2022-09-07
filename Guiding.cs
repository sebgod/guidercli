/*

MIT License

Copyright (c) 2018 Andy Galasso

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

*/

namespace PHD2;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text.Json;

public class SettleProgress
{
    public bool Done { get; set; }
    public double Distance { get; set; }
    public double SettlePx { get; set; }
    public double Time { get; set; }
    public double SettleTime { get; set; }
    public int Status { get; set; }
    public string? Error { get; set; }
}

public class GuideStats
{
    public double rms_tot { get; set; }
    public double rms_ra { get; set; }
    public double rms_dec { get; set; }
    public double peak_ra { get; set; }
    public double peak_dec { get; set; }

    public GuideStats Clone() => (GuideStats)MemberwiseClone();
}

public class SettleRequest
{
    public SettleRequest(double pixels, double time, double timeout)
    {
        Pixels = pixels;
        Time = time;
        Timeout = timeout;
    }

    public double Pixels { get; set; }
    public double Time { get; set; }
    public double Timeout { get; set; }
}

[Serializable]
public class GuiderException : Exception
{
    public GuiderException(string message) : base(message) { }
    public GuiderException(string message, Exception inner) : base(message, inner) { }

    protected GuiderException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) : base(info, context)
    {

    }
}

public interface IGuider : IDisposable
{
    /// <summary>
    /// connect to PHD2 -- you'll need to call Connect before calling any of the server API methods below
    /// </summary>
    void Connect();

    /// <summary>
    /// Checks if guider is connected (via TCP).
    /// </summary>
    /// <returns>true iff connected<returns>
    bool IsConnected { get; }

    /// <summary>
    /// disconnect from PHD2
    /// </summary>
    void Close();

    /// <summary>
    /// support raw JSONRPC method invocation. Generally you won't need to
    /// use this function as it is much more convenient to use the higher-level methods below
    /// </summary>
    /// <param name="method"></param>
    /// <param name="params"></param>
    /// <returns></returns>
    JsonDocument Call(string method, params object[] @params);

    /// <summary>
    /// Start guiding with the given settling parameters. PHD2 takes care of looping exposures,
    /// guide star selection, and settling. Call CheckSettling() periodically to see when settling
    /// is complete.
    /// </summary>
    /// <param name="settlePixels">settle threshold in pixels</param>
    /// <param name="settleTime">settle time in seconds</param>
    /// <param name="settleTimeout">settle timeout in seconds</param>
    void Guide(double settlePixels, double settleTime, double settleTimeout);

    /// <summary>
    /// Dither guiding with the given dither amount and settling parameters. Call <see cref="CheckSettling()"/>
    /// periodically to see when settling is complete.
    /// </summary>
    /// <param name="ditherPixels"></param>
    /// <param name="settlePixels"></param>
    /// <param name="settleTime"></param>
    /// <param name="settleTimeout"></param>
    void Dither(double ditherPixels, double settlePixels, double settleTime, double settleTimeout, bool raOnly = false);

    /// <summary>
    /// Check if phd2 is currently in the process of settling after a Guide or Dither
    /// Will throw if not connected.
    /// </summary>
    /// <returns>true iff settling</returns>
    bool IsSettling();

    /// <summary>
    /// Check if phd2 is currently looping exposures.
    /// Will throw if not connected.
    /// </summary>
    /// <returns>true iff looping</returns>
    bool IsLooping();

    /// <summary>
    /// Get the progress of settling
    /// </summary>
    /// <returns></returns>
    SettleProgress CheckSettling();

    // Get the guider statistics since guiding started. Frames captured while settling is in progress
    // are excluded from the stats.
    GuideStats? GetStats();

    // stop looping and guiding
    void StopCapture(uint timeoutSeconds = 10);

    /// <summary>
    /// start looping exposures
    /// </summary>
    /// <param name="timeoutSeconds">timeout after looping attempt is cancelled</param>
    void Loop(uint timeoutSeconds = 10);

    /// <summary>
    /// get the guider pixel scale in arc-seconds per pixel
    /// </summary>
    /// <returns>pixel scale of the guiding camera in arc-seconds per pixel</returns>
    double PixelScale();

    /// <summary>
    /// get a list of the Equipment Profile names
    /// </summary>
    /// <returns></returns>
    List<string> GetEquipmentProfiles();

    /// <summary>
    /// connect the equipment in an equipment profile
    /// </summary>
    /// <param name="profileName"></param>
    void ConnectEquipment(string profileName);

    /// <summary>
    /// disconnect equipment
    /// </summary>
    void DisconnectEquipment();

    /// <summary>
    /// get the AppState (https://github.com/OpenPHDGuiding/phd2/wiki/EventMonitoring#appstate)
    /// and current guide error
    /// </summary>
    /// <param name="appState">application runtime state</param>
    /// <param name="avgDist">a smoothed average of the guide distance in pixels</param>
    void GetStatus(out string? appState, out double avgDist);

    /// <summary>
    /// check if currently guiding
    /// Will throw if not connected.
    /// </summary>
    /// <returns></returns>
    bool IsGuiding();

    /// <summary>
    /// pause guiding (looping exposures continues)
    /// </summary>
    void Pause();

    /// <summary>
    /// un-pause guiding
    /// </summary>
    void Unpause();

    /// <summary>
    /// save the current guide camera frame (FITS format), returning the name of the file.
    /// The caller will need to remove the file when done.
    /// </summary>
    /// <returns></returns>
    string? SaveImage();

    /// <summary>
    /// Event that is triggered when an unknown event is received from the guiding application.
    /// </summary>
    event EventHandler<UnhandledEventArgs>? UnhandledEvent;

    /// <summary>
    /// Event that is triggered when an exception occurs.
    /// </summary>
    event EventHandler<GuidingErrorEventArgs>? GuidingErrorEvent;
}

public class UnhandledEventArgs : EventArgs
{
    public UnhandledEventArgs(string @event, string payload)
    {
        Event = @event;
        Payload = payload;
    }

    public string Event { get; }

    public string Payload { get; }
}

public class GuidingErrorEventArgs : EventArgs
{
    public GuidingErrorEventArgs(string msg, Exception? ex = null)
    {
        Message = msg;
        Exception = ex;
    }

    public string Message { get; }

    public Exception? Exception { get; }
}

class GuiderConnection : IDisposable
{
    static readonly byte[] CRLF = new byte[2] { (byte)'\r', (byte)'\n' };

    private TcpClient? _tcpClient;
    private StreamReader? _streamReader;

    public GuiderConnection()
    {
        // empty
    }

    public void Connect(string hostname, ushort port)
    {
        try
        {
            _tcpClient = new TcpClient(hostname, port);
            _streamReader = new StreamReader(_tcpClient.GetStream());
        }
        catch
        {
            Close();
            throw;
        }
    }

    public virtual void OnConnectionError(GuidingErrorEventArgs eventArgs)
    {

    }

    public void Dispose()
    {
        Close();
    }

    public void Close()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _streamReader?.Close();
            _streamReader?.Dispose();
            _streamReader = null;

            _tcpClient?.Close();
            _tcpClient = null;
        }
    }

    public bool IsConnected => _tcpClient?.Connected == true;

    public string? ReadLine() => _streamReader?.ReadLine();

    public void WriteLine(ReadOnlyMemory<byte> jsonlUtf8Bytes)
    {
        var stream = _tcpClient?.GetStream();
        if (stream != null && stream.CanWrite)
        {
            stream.Write(jsonlUtf8Bytes.Span);
            stream.Write(CRLF);
            stream.Flush();
        }
    }

    public void Terminate() => _tcpClient?.Close();
}

class Accum
{
    uint n;
    double a;
    double q;
    double peak;

    public Accum() {
        Reset();
    }
    public void Reset() {
        n = 0;
        a = q = peak = 0;
    }
    public void Add(double x) {
        double ax = Math.Abs(x);
        if (ax > peak) peak = ax;
        ++n;
        double d = x - a;
        a += d / n;
        q += (x - a) * d;
    }
    public double Mean() {
        return a;
    }
    public double Stdev() {
        return n >= 1 ? Math.Sqrt(q / n) : 0.0;
    }
    public double Peak() {
        return peak;
    }
}

public class GuiderImpl : IGuider
{
    System.Threading.Thread? m_worker;
    volatile bool m_terminate;
    readonly object m_sync = new object();
    JsonDocument? m_response;

    string Host { get; }
    uint Instance { get; }
    GuiderConnection Connection { get; }
    Accum AccumRA { get; } = new Accum();
    Accum AccumDEC { get; } = new Accum();
    bool IsAccumActive { get; set; }
    double SettlePixels { get; set; }
    string? AppState { get; set; }
    double AvgDist { get; set; }
    GuideStats? Stats { get; set; }
    string? Version { get; set; }
    string? PHDSubvVersion { get; set; }
    SettleProgress? Settle { get; set; }

    private void Worker()
    {
        string? line = null;
        try
        {
            while (!m_terminate)
            {
                try
                {
                    line = Connection.ReadLine();
                }
                catch (Exception ex)
                {
                    OnGuidingErrorEvent(new GuidingErrorEventArgs($"Error {ex.Message} while reading from input stream", ex));
                    // use recovery logic
                }

                if (line == null)
                {
                    // phd2 disconnected
                    // todo: re-connect (?)
                    m_terminate = true;
                    break;
                }

                JsonDocument j;
                try
                {
                    j = JsonDocument.Parse(line);
                }
                catch (JsonException ex)
                {
                    OnGuidingErrorEvent(new GuidingErrorEventArgs(string.Format("ignoring invalid json from server: {0}: {1}", ex.Message, line), ex));
                    continue;
                }

                if (j.RootElement.TryGetProperty("jsonrpc", out var _))
                {
                    lock (m_sync)
                    {
                        m_response?.Dispose();
                        m_response = j;
                        System.Threading.Monitor.Pulse(m_sync);
                    }
                }
                else
                {
                    HandleEvent(j);
                }
            }
        }
        catch (Exception ex)
        {
            OnGuidingErrorEvent(new GuidingErrorEventArgs($"caught exception in worker thread while processing: {line}: {ex.Message}", ex));
        }
        finally
        {
            Connection.Terminate();
        }
    }

    private static void WorkerEntryPoint(object? obj)
    {
        if (obj is GuiderImpl impl) {
            impl.Worker();
        }
    }

    private void HandleEvent(JsonDocument @event)
    {
        string? eventName = @event.RootElement.GetProperty("Event").GetString();

        if (eventName == "AppState")
        {
            lock (m_sync)
            {
                AppState = @event.RootElement.GetProperty("State").GetString();
                if (IsGuidingAppState(AppState))
                    AvgDist = 0.0;   // until we get a GuideStep event
            }
        }
        else if (eventName == "Version")
        {
            lock (m_sync)
            {
                Version = @event.RootElement.GetProperty("PHDVersion").GetString();
                PHDSubvVersion = @event.RootElement.GetProperty("PHDSubver").GetString();
            }
        }
        else if (eventName == "StartGuiding")
        {
            IsAccumActive = true;
            AccumRA.Reset();
            AccumDEC.Reset();

            GuideStats stats = AccumulateGuidingStats(AccumRA, AccumDEC);

            lock (m_sync)
            {
                Stats = stats;
            }
        }
        else if (eventName == "GuideStep")
        {
            GuideStats? stats = null;
            if (IsAccumActive)
            {
                AccumRA.Add(@event.RootElement.GetProperty("RADistanceRaw").GetDouble());
                AccumDEC.Add(@event.RootElement.GetProperty("DECDistanceRaw").GetDouble());
                stats = AccumulateGuidingStats(AccumRA, AccumDEC);
            }

            lock (m_sync)
            {
                AppState = "Guiding";
                AvgDist = @event.RootElement.GetProperty("AvgDist").GetDouble();
                if (IsAccumActive)
                    Stats = stats;
            }
        }
        else if (eventName == "SettleBegin")
        {
            IsAccumActive = false;  // exclude GuideStep messages from stats while settling
        }
        else if (eventName == "Settling")
        {
            var settingProgress = new SettleProgress
            {
                Done = false,
                Distance = @event.RootElement.GetProperty("Distance").GetDouble(),
                SettlePx = SettlePixels,
                Time = @event.RootElement.GetProperty("Time").GetDouble(),
                SettleTime = @event.RootElement.GetProperty("SettleTime").GetDouble(),
                Status = 0
            };
            lock (m_sync)
            {
                Settle = settingProgress;
            }
        }
        else if (eventName == "SettleDone")
        {
            IsAccumActive = true;
            AccumRA.Reset();
            AccumDEC.Reset();

            GuideStats stats = AccumulateGuidingStats(AccumRA, AccumDEC);

            var settleProgress = new SettleProgress
            {
                Done = true,
                Status = @event.RootElement.GetProperty("Status").GetInt32(),
                Error = @event.RootElement.TryGetProperty("Error", out var error) ? error.GetString() : null
            };

            lock (m_sync)
            {
                Settle = settleProgress;
                Stats = stats;
            }
        }
        else if (eventName == "Paused")
        {
            lock (m_sync)
            {
                AppState = "Paused";
            }
        }
        else if (eventName == "StartCalibration")
        {
            lock (m_sync)
            {
                AppState = "Calibrating";
            }
        }
        else if (eventName == "LoopingExposures")
        {
            lock (m_sync)
            {
                AppState = "Looping";
            }
        }
        else if (eventName == "LoopingExposuresStopped" || eventName == "GuidingStopped")
        {
            lock (m_sync)
            {
                AppState = "Stopped";
            }
        }
        else if (eventName == "StarLost")
        {
            lock (m_sync)
            {
                AppState = "LostLock";
                AvgDist = @event.RootElement.GetProperty("AvgDist").GetDouble();
            }
        }
        else if (eventName != null)
        {
            OnUnhandledEvent(new UnhandledEventArgs(eventName, @event.RootElement.GetRawText()));
        }
    }

    static (Utf8JsonWriter jsonWriter, ArrayBufferWriter<byte> buffer) StartJsonRPCCall(string method)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var req = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false });

        req.WriteStartObject();
        req.WriteString("method", method);
        req.WriteNumber("id", 1);

        return (req, buffer);
    }

    static ReadOnlyMemory<byte> EndJsonRPCCall(Utf8JsonWriter jsonWriter, ArrayBufferWriter<byte> buffer)
    {
        jsonWriter.WriteEndObject();
        jsonWriter.Dispose();
        return buffer.WrittenMemory;
    }

    static ReadOnlyMemory<byte> MakeJsonRPCCall(string method, params object[] @params)
    {
        var (req, buffer) = StartJsonRPCCall(method);

        if (@params != null && @params.Length > 0) {
            req.WritePropertyName("params");

            req.WriteStartArray();
            foreach (var param in @params)
            {
                if (param is null)
                {
                    req.WriteNullValue();
                }
                else
                {
                    var typeCode = Type.GetTypeCode(param.GetType());
                    switch (typeCode)
                    {
                        case TypeCode.Boolean:
                            req.WriteBooleanValue((bool)param);
                            break;

                        case TypeCode.Int32:
                            req.WriteNumberValue((int)param);
                            break;

                        case TypeCode.Int64:
                            req.WriteNumberValue((long)param);
                            break;

                        case TypeCode.Single:
                            req.WriteNumberValue((float)param);
                            break;

                        case TypeCode.Double:
                            req.WriteNumberValue((double)param);
                            break;

                        case TypeCode.Object:
                            if (param is SettleRequest settleRequest)
                            {
                                req.WriteStartObject();
                                req.WriteNumber("pixels", settleRequest.Pixels);
                                req.WriteNumber("time", settleRequest.Time);
                                req.WriteNumber("timeout", settleRequest.Timeout);
                                req.WriteEndObject();
                            }
                            else
                            {
                                throw new ArgumentException($"Param {param} of type {param.GetType()} which is an object is not handled", nameof(@params));
                            }
                            break;

                        default:
                            throw new ArgumentException($"Param {param} of type {param.GetType()} which is type code {typeCode} is not handled", nameof(@params));
                    }
                }
            }
            req.WriteEndArray();
        }

        return EndJsonRPCCall(req, buffer);
    }

    static bool IsFailedResponse(JsonDocument response)
    {
        return response.RootElement.TryGetProperty("error", out _);
    }

    public GuiderImpl(string hostname, uint phd2_instance)
    {
        Host = hostname;
        Instance = phd2_instance;
        Connection = new GuiderConnection();
    }

    public void Close() => Dispose();

    public void Dispose() => Dispose(true);

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (m_worker != null)
            {
                m_terminate = true;
                Connection.Terminate();
                m_worker?.Join();
                m_worker = null;
            }

            Connection.Close();

            GC.SuppressFinalize(this);
        }
    }

    public void Connect()
    {
        Close();

        ushort port = (ushort)(4400 + Instance - 1);

        try
        {
            Connection.Connect(Host, port);
        }
        catch (Exception e)
        {
            string errorMsg = string.Format("Could not connect to PHD2 instance {0} on {1}:{2}", Instance, Host, port);
            OnGuidingErrorEvent(new GuidingErrorEventArgs(errorMsg, e));
            throw new GuiderException(errorMsg);
        }

        m_terminate = false;

        var thread = new System.Threading.Thread(WorkerEntryPoint);
        thread.Start(this);
        m_worker = thread;
    }

    static GuideStats AccumulateGuidingStats(Accum ra, Accum dec) => new GuideStats()
    {
        rms_ra = ra.Stdev(),
        rms_dec = dec.Stdev(),
        peak_ra = ra.Peak(),
        peak_dec = dec.Peak()
    };

    static bool IsGuidingAppState(string? appState) => appState == "Guiding" || appState == "LostLock";

    public JsonDocument Call(string method, params object[] @params)
    {
        var memory = MakeJsonRPCCall(method, @params);

        // send request
        Connection.WriteLine(memory);

        // wait for response

        lock (m_sync)
        {
            while (m_response == null)
                System.Threading.Monitor.Wait(m_sync);

            JsonDocument response = m_response;
            m_response = null;

            if (IsFailedResponse(response))
            {
                throw new GuiderException((response.RootElement.GetProperty("error").TryGetProperty("message", out var message) ? message.GetString() : null) ?? "error response did not contain error message");
            }

            return response;
        }
    }

    public bool IsConnected => Connection.IsConnected;

    void CheckConnected()
    {
        if (!Connection.IsConnected)
            throw new GuiderException("PHD2 Server disconnected");
    }

    public void Guide(double settlePixels, double settleTime, double settleTimeout)
    {
        CheckConnected();

        var settleProgress = new SettleProgress
        {
            Done = false,
            Distance = 0.0,
            SettlePx = settlePixels,
            Time = 0.0,
            SettleTime = settleTime,
            Status = 0
        };

        lock (m_sync)
        {
            if (Settle != null && !Settle.Done)
                throw new GuiderException("cannot guide while settling");
            Settle = settleProgress;
        }

        try
        {
            using var response = Call("guide", new SettleRequest(settlePixels, settleTime, settleTimeout), false /* don't force calibration */);
            SettlePixels = settlePixels;
        }
        catch (Exception ex)
        {
            var guidingErrorEventArgs = new GuidingErrorEventArgs($"while calling guide({settlePixels}, {settleTime}, {settleTimeout}): {ex.Message}", ex);
            OnGuidingErrorEvent(guidingErrorEventArgs);
            // failed - remove the settle state
            lock (m_sync)
            {
                Settle = null;
            }
            var inner = guidingErrorEventArgs.Exception;
            if (inner is null) {
                throw new GuiderException(guidingErrorEventArgs.Message);
            } else {
                throw new GuiderException(guidingErrorEventArgs.Message, inner);
            }
        }
    }

    public void Dither(double ditherPixels, double settlePixels, double settleTime, double settleTimeout, bool raOnly = false)
    {
        CheckConnected();

        var settleProgress = new SettleProgress()
        {
            Done = false,
            Distance = ditherPixels,
            SettlePx = settlePixels,
            Time = 0.0,
            SettleTime = settleTime,
            Status = 0
        };

        lock (m_sync)
        {
            if (Settle != null && !Settle.Done)
                throw new GuiderException("cannot dither while settling");

            Settle = settleProgress;
        }

        try
        {
            using var response = Call("dither", ditherPixels, raOnly, new SettleRequest(settlePixels, settleTime, settleTimeout));
            SettlePixels = settlePixels;
        }
        catch (Exception ex)
        {
            var guidingErrorEventArgs = new GuidingErrorEventArgs(
                $"while calling dither(ditherPixels: {ditherPixels}, raOnly: {raOnly}, (settlePixels: {settlePixels}, settleTime: {settleTime}, settleTimeout: {settleTimeout}): {ex.Message}", ex);
            OnGuidingErrorEvent(guidingErrorEventArgs);
            // call failed - remove the settle state
            lock (m_sync)
            {
                Settle = null;
            }
            var inner = guidingErrorEventArgs.Exception;
            if (inner is null) {
                throw new GuiderException(guidingErrorEventArgs.Message);
            } else {
                throw new GuiderException(guidingErrorEventArgs.Message, inner);
            }
        }
    }

    public bool IsSettling()
    {
        CheckConnected();

        lock (m_sync)
        {
            if (Settle != null)
            {
                return !Settle.Done;
            }
        }

        // for app init, initialize the settle state to a consistent value
        // as if Guide had been called

        using var settlingResponse = Call("get_settling");

        bool isSettling = settlingResponse.RootElement.GetProperty("result").GetBoolean();

        if (isSettling)
        {
            var settleProgress = new SettleProgress
            {
                Done = false,
                Distance = -1.0,
                SettlePx = 0.0,
                Time = 0.0,
                SettleTime = 0.0,
                Status = 0
            };
            lock (m_sync)
            {
                if (Settle == null)
                    Settle = settleProgress;
            }
        }

        return isSettling;
    }

    public SettleProgress CheckSettling()
    {
        CheckConnected();

        var settleProgress = new SettleProgress();

        lock (m_sync)
        {
            if (Settle == null)
                throw new GuiderException("not settling");

            if (Settle.Done)
            {
                // settle is done
                settleProgress.Done = true;
                settleProgress.Status = Settle.Status;
                settleProgress.Error = Settle.Error;
                Settle = null;
            }
            else
            {
                // settle in progress
                settleProgress.Done = false;
                settleProgress.Distance = Settle.Distance;
                settleProgress.SettlePx = SettlePixels;
                settleProgress.Time = Settle.Time;
                settleProgress.SettleTime = Settle.SettleTime;
            }
        }

        return settleProgress;
    }

    public GuideStats? GetStats()
    {
        CheckConnected();

        GuideStats? stats;
        lock (m_sync)
        {
            stats = Stats?.Clone();
        }
        if (stats is not null) {
            stats.rms_tot = Math.Sqrt(stats.rms_ra * stats.rms_ra + stats.rms_dec * stats.rms_dec);
        }
        return stats;
    }

    public void StopCapture(uint timeoutSeconds)
    {
        using var stopCaptureResponse = Call("stop_capture");

        for (uint i = 0; i < timeoutSeconds; i++)
        {
            string? appstate;
            lock (m_sync)
            {
                appstate = AppState;
            }
            Debug.WriteLine(string.Format("StopCapture: AppState = {0}", appstate));
            if (appstate == "Stopped")
                return;

            System.Threading.Thread.Sleep(1000);
            CheckConnected();
        }
        Debug.WriteLine("StopCapture: timed-out waiting for stopped");

        // hack! workaround bug where PHD2 sends a GuideStep after stop request and fails to send GuidingStopped
        using var appStateResponse = Call("get_app_state");
        var appState = appStateResponse.RootElement.GetProperty("result").GetString();

        lock (m_sync)
        {
            AppState = appState;
        }

        if (appState == "Stopped")
            return;
        // end workaround

        throw new GuiderException(string.Format("guider did not stop capture after {0} seconds!", timeoutSeconds));
    }

    public void Loop(uint timeoutSeconds)
    {
        CheckConnected();

        // already looping?
        lock (m_sync)
        {
            if (AppState == "Looping")
                return;
        }

        using var exposureResponse = Call("get_exposure");
        int exposure = exposureResponse.RootElement.GetProperty("result").GetInt32();

        using var loopingResponse = Call("loop");

        System.Threading.Thread.Sleep(exposure);

        for (uint i = 0; i < timeoutSeconds; i++)
        {
            lock (m_sync)
            {
                if (AppState == "Looping")
                    return;
            }

            System.Threading.Thread.Sleep(1000);
            CheckConnected();
        }

        throw new GuiderException("timed-out waiting for guiding to start looping");
    }

    public double PixelScale()
    {
        using var response = Call("get_pixel_scale");
        return response.RootElement.GetProperty("result").GetDouble();
    }

    public List<string> GetEquipmentProfiles()
    {
        using var response = Call("get_profiles");

        var profiles = new List<string>();
        var jsonResultArray = response.RootElement.GetProperty("result");
        foreach (var item in jsonResultArray.EnumerateArray())
        {
            var name = item.GetProperty("name").GetString();
            if (!string.IsNullOrWhiteSpace(name)) {
                profiles.Add(name.Trim());
            }
        }

        return profiles;
    }

    static readonly uint DEFAULT_STOPCAPTURE_TIMEOUT = 10;

    public event EventHandler<UnhandledEventArgs>? UnhandledEvent;

    protected virtual void OnUnhandledEvent(UnhandledEventArgs eventArgs) => UnhandledEvent?.Invoke(this, eventArgs);

    public event EventHandler<GuidingErrorEventArgs>? GuidingErrorEvent;

    protected virtual void OnGuidingErrorEvent(GuidingErrorEventArgs eventArgs) => GuidingErrorEvent?.Invoke(this, eventArgs);

    public void ConnectEquipment(string profileName)
    {
        using var profileResponse = Call("get_profile");

        var activeProfile = profileResponse.RootElement.GetProperty("result");

        if (activeProfile.GetProperty("name").GetString() != profileName)
        {
            using var profilesResponse = Call("get_profiles");
            var profiles = profilesResponse.RootElement.GetProperty("result");
            int profileId = -1;
            foreach (var profile in profiles.EnumerateArray())
            {
                var name = profile.GetProperty("name").GetString();
                Debug.WriteLine(string.Format("found profile {0}", name));
                if (name == profileName)
                {
                    profileId = profile.TryGetProperty("id", out var id) ? id.GetInt32() : -1;
                    Debug.WriteLine(String.Format("found profid {0}", profileId));
                    break;
                }
            }

            if (profileId == -1)
                throw new GuiderException("invalid phd2 profile name: " + profileName);

            StopCapture(DEFAULT_STOPCAPTURE_TIMEOUT);

            using var disconnectResponse = Call("set_connected", false);
            using var updateProfileResponse = Call("set_profile", profileId);
        }

        using var connectResponse = Call("set_connected", true);
    }

    public void DisconnectEquipment()
    {
        StopCapture(DEFAULT_STOPCAPTURE_TIMEOUT);
        using var disconnectResponse = Call("set_connected", false);
    }

    public void GetStatus(out string? appState, out double avgDist)
    {
        CheckConnected();

        lock (m_sync)
        {
            appState = AppState;
            avgDist = AvgDist;
        }
    }

    public bool IsGuiding()
    {
        GetStatus(out string? appState, out double _ /* average distance */);
        return IsGuidingAppState(appState);
    }

    public bool IsLooping()
    {
        GetStatus(out string? appState, out double _ /* average distance */);
        return appState == "Looping";
    }

    public void Pause()
    {
        using var response = Call("set_paused", true);
    }

    public void Unpause()
    {
        using var response = Call("set_paused", false);
    }

    public string? SaveImage()
    {
        using var response = Call("save_image");
        return response.RootElement.GetProperty("result").GetProperty("filename").GetString();
    }
}