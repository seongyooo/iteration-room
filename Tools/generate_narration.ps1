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

# $rate: -2 is the announcement cadence. The synthesizer's default clip is what made it sound like
# a screen reader rather than a PA - slowing it down is most of what buys the delivery. The
# countdown digits can only go to -1, because each one has to finish inside a one-second slot
# before the next digit replaces it.
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

# Same, but the text is SSML rather than plain. Needed for the iteration lines, where the number has
# to be lifted in pitch on its own - $synth.Rate is a whole-clip setting and cannot do that.
# $synth.Rate still applies underneath as the baseline tempo.
function Write-Ssml([string]$body, [string]$file, [int]$rate) {
    $ssml = @"
<speak version="1.0" xmlns="http://www.w3.org/2001/10/synthesis" xml:lang="en-US">$body</speak>
"@
    $synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
    try {
        $synth.SelectVoice($voiceName)
        $synth.Rate = $rate
        $synth.Volume = 100
        $synth.SetOutputToWaveFile((Join-Path $dir $file), $fmt)
        $synth.SpeakSsml($ssml)
    } finally {
        $synth.Dispose()
    }
}

$count = 0

# "Iteration N, 60 seconds remaining." as one clip per N rather than stitching a number onto a
# shared tail - the synthesizer gets the sentence intonation right only when it sees the whole
# sentence. 30 covers far more iterations than a prototype run reaches; past that the generic
# line below stands in.
#
# The number is lifted in pitch and left rising, then a beat before the informational tail - so it
# lands as "Iteration one!" rather than as a label being read off a list.
#
# Note the "?" and that it is NOT a mistake. Getting a rising ending out of SAPI takes a question
# mark: the terminal contour is chosen from sentence punctuation, and it overrides everything else.
# Measured on Zira, over the number, at rate -2:
#     "1!"                      220 -> 160 Hz   falls (declarative terminal, exclamation ignored)
#     prosody contour="..."     202 -> 138 Hz   worse; SAPI ignores the attribute
#     "1?"                      182 -> 232 Hz   rises
#     pitch +35% and "1?"       179 -> 259 Hz   rises furthest        <- this
# The "?" is never spoken, and because it closes the sentence there, the rise sits on the number.
# "60 seconds remaining." is then its own sentence and keeps its normal falling ending - putting the
# rise at the end of the whole line instead would turn "remaining" into a question.
foreach ($n in 1..30) {
    $body = "Iteration <prosody pitch=""+35%"">$n</prosody>?<break time=""350ms"" /> 60 seconds remaining."
    Write-Ssml $body ("voice_iteration_{0:00}.wav" -f $n) -2
    $count++
}

Write-Ssml "New <prosody pitch=""+35%"">iteration</prosody>?<break time=""350ms"" /> 60 seconds remaining." "voice_iteration_generic.wav" -2
Write-Line "New cycle initialized." "voice_new_cycle.wav" -2
# Spoken when the player ends a cycle themselves instead of running the clock out. The distinction
# matters: a voluntary end skips the countdown entirely, which is otherwise the loop's loudest beat.
Write-Line "Cycle terminated." "voice_cycle_terminated.wav" -2
$count += 3

Write-Line "10 seconds remaining." "voice_ten_seconds.wav" -1
$count++

$digits = @{ 9 = "Nine."; 8 = "Eight."; 7 = "Seven."; 6 = "Six."; 5 = "Five."
             4 = "Four."; 3 = "Three."; 2 = "Two."; 1 = "One." }
foreach ($d in 9..1) {
    Write-Line $digits[$d] ("voice_count_{0}.wav" -f $d) -1
    $count++
}

Write-Host "Wrote $count clips to $dir"
