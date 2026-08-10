# Regenerates every PA-announcer voice line into Assets/Audio/Voice as 16-bit mono WAV.
#
# Placeholder narration, deliberately produced by the Windows built-in synthesizer (Zira) rather
# than sourced as audio files: the whole project is reproducible from scripts, and this keeps the
# voice track in that category. It costs nothing, runs offline, and the flat synthetic delivery
# happens to suit a facility PA system. Swapping in better-acted lines later means dropping files
# with the same names into the same folder - nothing in the C# refers to how they were made.
#
# Run:  powershell -ExecutionPolicy Bypass -File Tools\generate_narration.ps1

Add-Type -AssemblyName System.Speech

$voiceName = "Microsoft Zira Desktop"
$dir = Join-Path $PSScriptRoot "..\Assets\Audio\Voice"
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$dir = (Resolve-Path $dir).Path

# 22 kHz mono is plenty for a tannoy voice and keeps the clips small.
$fmt = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(
    22050,
    [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen,
    [System.Speech.AudioFormat.AudioChannel]::Mono)

# $rate: -1 is a measured announcement cadence, used where there is room for it. The countdown
# lines run at 0 because each one has to fit inside a one-second slot before the next digit
# replaces it.
function Write-Line([string]$text, [string]$file, [int]$rate) {
    $synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
    try {
        $synth.SelectVoice($voiceName)
        $synth.Rate = $rate
        $synth.Volume = 100
        $synth.SetOutputToWaveFile((Join-Path $dir $file), $fmt)
        $synth.Speak($text)
    } finally {
        $synth.Dispose()
    }
}

$count = 0

# "Iteration N, 60 seconds remaining." as one clip per N rather than stitching a number onto a
# shared tail - the synthesizer gets the sentence intonation right only when it sees the whole
# sentence. 30 covers far more iterations than a prototype run reaches; past that the generic
# line below stands in.
foreach ($n in 1..30) {
    Write-Line "Iteration $n, 60 seconds remaining." ("voice_iteration_{0:00}.wav" -f $n) -1
    $count++
}

Write-Line "New iteration, 60 seconds remaining." "voice_iteration_generic.wav" -1
Write-Line "New cycle initialized." "voice_new_cycle.wav" -1
$count += 2

Write-Line "10 seconds remaining." "voice_ten_seconds.wav" 0
$count++

$digits = @{ 9 = "Nine."; 8 = "Eight."; 7 = "Seven."; 6 = "Six."; 5 = "Five."
             4 = "Four."; 3 = "Three."; 2 = "Two."; 1 = "One." }
foreach ($d in 9..1) {
    Write-Line $digits[$d] ("voice_count_{0}.wav" -f $d) 0
    $count++
}

Write-Host "Wrote $count clips to $dir"
