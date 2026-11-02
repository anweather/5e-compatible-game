# Renders the voice test set: a dozen game asks as 16 kHz mono WAVs with Windows' own voices, some
# quieter or with noise added, each with a transcript of its words and when they fall. The output
# is git-ignored. RecordedSpeech plays the transcripts through the speech port in the voice probe;
# a Whisper adapter will transcribe the WAVs, with the transcripts as the expected text.
#
# It writes files only: nothing plays, and no audio device or Windows setting changes.
#
#   powershell -ExecutionPolicy Bypass -File tools/voice-samples/make.ps1
$ErrorActionPreference = 'Stop'
$out = Join-Path $PSScriptRoot 'out'
New-Item -ItemType Directory -Force $out | Out-Null

Add-Type -AssemblyName System.Speech
Add-Type -ReferencedAssemblies System.Speech -TypeDefinition @'
using System.Collections.Generic;
using System.Globalization;
using System.Speech.AudioFormat;
using System.Speech.Synthesis;

public static class VoiceSample
{
    // PowerShell can't run a script block on the synthesiser's thread, so the words are noted here.
    public static string Render(string voice, string text, string wav)
    {
        var words = new List<string>();
        using (var speech = new SpeechSynthesizer())
        {
            speech.SelectVoice(voice);
            speech.SetOutputToWaveFile(wav, new SpeechAudioFormatInfo(16000, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
            speech.SpeakProgress += (_, e) => words.Add(e.AudioPosition.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture) + " " + e.Text);
            speech.Speak(text);
            speech.SetOutputToNull();
        }
        return string.Join("\n", words);
    }
}
'@

$david = 'Microsoft David Desktop'
$zira = 'Microsoft Zira Desktop'
$samples = @(
    @{ Id = 'fire-near-me-green'; Text = 'make the fire near me green'; Voice = $david; Effect = 'clean' }
    @{ Id = 'day-time'; Text = 'make it day time'; Voice = $zira; Effect = 'clean' }
    @{ Id = 'all-fire-blue'; Text = 'make all fire blue'; Voice = $david; Effect = 'clean' }
    @{ Id = 'rain-off'; Text = 'turn the rain off'; Voice = $zira; Effect = 'clean' }
    @{ Id = 'storm'; Text = 'start a storm with lightning'; Voice = $david; Effect = 'clean' }
    @{ Id = 'snow'; Text = 'make it snow in the village'; Voice = $zira; Effect = 'clean' }
    @{ Id = 'sunset-quiet'; Text = 'make it sunset'; Voice = $david; Effect = 'quiet' }
    @{ Id = 'bigger-quiet'; Text = 'make me twice as big'; Voice = $zira; Effect = 'quiet' }
    @{ Id = 'freeze-quiet'; Text = 'freeze the river'; Voice = $david; Effect = 'quiet' }
    @{ Id = 'smoke-noisy'; Text = 'make the smoke purple'; Voice = $zira; Effect = 'noisy' }
    @{ Id = 'undo-noisy'; Text = 'undo the last change'; Voice = $david; Effect = 'noisy' }
    @{ Id = 'fog-noisy'; Text = 'clear the fog over the lake'; Voice = $zira; Effect = 'noisy' }
)

function Apply-Effect($wav, $effect, $seconds)
{
    if ($effect -eq 'clean') { return }
    $raw = "$wav.raw.wav"
    Move-Item -Force $wav $raw
    $format = @('-ar', '16000', '-ac', '1', '-c:a', 'pcm_s16le', $wav)
    if ($effect -eq 'quiet') {
        & ffmpeg -v error -y -i $raw -filter:a 'volume=0.25' @format
    } else {
        $noise = "anoisesrc=d=${seconds}:c=pink:a=0.03:r=16000[n];[0:a][n]amix=inputs=2:duration=first:normalize=0"
        & ffmpeg -v error -y -i $raw -filter_complex $noise @format
    }
    if ($LASTEXITCODE -ne 0) { throw "ffmpeg failed on $wav" }
    Remove-Item $raw
}

foreach ($sample in $samples) {
    $wav = Join-Path $out "$($sample.Id).wav"
    $words = [VoiceSample]::Render($sample.Voice, $sample.Text, $wav)
    $seconds = ((Get-Item $wav).Length - 46) / 32000
    $time = $seconds.ToString('0.000', [Globalization.CultureInfo]::InvariantCulture)
    Apply-Effect $wav $sample.Effect $time
    [IO.File]::WriteAllText((Join-Path $out "$($sample.Id).txt"), "seconds $time`n$words`n")
    Write-Output "$($sample.Id): $time s, $($sample.Effect)"
}
