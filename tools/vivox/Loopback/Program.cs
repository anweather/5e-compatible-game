// Records what the default output device actually plays (WASAPI loopback) and writes its RMS level
// every 100 ms with the clock time, so a VivoxSpike listener's logged windows can be matched to what
// was heard. A dev tool for the proximity voice spike (docs/handoffs/briefs/vivox-canned.md).
//   dotnet run --project tools/vivox/Loopback -- <out.tsv> <seconds>
using NAudio.CoreAudioApi;
using NAudio.Wave;

string path = args.Length > 0 ? args[0] : "loopback.tsv";
int seconds = args.Length > 1 ? int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 180;
using var device = new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
using var capture = new WasapiLoopbackCapture(device);
using var output = new StreamWriter(path);
var format = capture.WaveFormat;
int frame = format.SampleRate / 10;
double sum = 0;
int count = 0;
Console.WriteLine($"Loopback of {device.FriendlyName}: {format}");
output.WriteLine("time\trms");
capture.DataAvailable += (_, e) =>
{
    // Shared-mode loopback is 32-bit float; every channel counts.
    for (int i = 0; i + 4 <= e.BytesRecorded; i += 4)
    {
        float s = BitConverter.ToSingle(e.Buffer, i);
        sum += s * s;
        if (++count < frame * format.Channels) continue;
        output.WriteLine($"{DateTime.Now:HH:mm:ss.fff}\t{Math.Sqrt(sum / count):0.000000}");
        sum = 0;
        count = 0;
    }
};
capture.StartRecording();
Thread.Sleep(TimeSpan.FromSeconds(seconds));
capture.StopRecording();
