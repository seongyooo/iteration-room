# Regenerates every PA-announcer voice line as 16-bit mono WAV.
#
# Placeholder narration, deliberately produced by the Windows built-in synthesizer rather than
# sourced as audio files: the whole project is reproducible from scripts, and this keeps the voice
# track in that category. It costs nothing, runs offline, and the flat synthetic delivery happens to
# suit a facility PA system. Swapping in better-acted lines later means dropping files with the same
# names into the same folder - nothing in the C# refers to how they were made.
#
# TWO LANGUAGES, TWO FOLDERS, ONE SET OF FILENAMES.
#   en -> Assets/Audio/Voice        (Zira,  en-US)
#   ko -> Assets/Audio/Voice/ko     (Heami, ko-KR)
# English stays at the root rather than moving to Voice/en, so nothing that already points at these
# files has to be found and repointed, and a build with no Korean folder is exactly today's build.
# `SceneBuilder` wires BOTH sets into the scene and `NarrationDirector` picks one at runtime, so the
# language can change without a rebuild.
#
# **THIS FILE MUST KEEP ITS UTF-8 BOM.** Windows PowerShell 5.1 reads a .ps1 without one as ANSI,
# so every Hangul string below arrives at the synthesizer mangled - and it fails SILENTLY in the
# worst way: the mojibake still synthesises, so most clips come out as confident nonsense and only
# a few land as zero-length files. First pass at the Korean set lost seven countdown digits to this
# and the other 38 clips were garbage that looked fine. If the Korean lines ever go strange again,
# check the first three bytes for EF BB BF before suspecting the voice.
#
# Run:  powershell -ExecutionPolicy Bypass -File Tools\generate_narration.ps1            # both
#       powershell -ExecutionPolicy Bypass -File Tools\generate_narration.ps1 -Language ko

param([ValidateSet("en", "ko", "both")] [string]$Language = "both")

Add-Type -AssemblyName System.Speech

# 22 kHz mono is plenty for a tannoy voice and keeps the clips small.
$fmt = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(
    22050,
    [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen,
    [System.Speech.AudioFormat.AudioChannel]::Mono)

# $rate: -2 is the announcement cadence. The synthesizer's default clip is what made it sound like
# a screen reader rather than a PA - slowing it down is most of what buys the delivery. The
# countdown digits can only go to -1, because each one has to finish inside a one-second slot
# before the next digit replaces it.
function Write-Line([string]$voice, [string]$dir, [string]$text, [string]$file, [int]$rate) {
    $synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
    try {
        $synth.SelectVoice($voice)
        $synth.Rate = $rate
        $synth.Volume = 100
        $synth.SetOutputToWaveFile((Join-Path $dir $file), $fmt)
        $synth.Speak($text)
    } finally {
        # CLOSE THE FILE BEFORE DISPOSING, and this is not belt-and-braces. Dispose alone does not
        # reliably flush the wave writer when clips are generated back to back: a first pass at the
        # Korean set wrote seven of the nine countdown digits as valid WAV headers with zero frames
        # of audio, while every one of them synthesised correctly on its own. SetOutputToNull is what
        # detaches and closes the stream.
        $synth.SetOutputToNull()
        $synth.Dispose()
    }
}

# Same, but the text is SSML rather than plain. Needed for the iteration lines, where the number has
# to be lifted in pitch on its own - $synth.Rate is a whole-clip setting and cannot do that.
# $synth.Rate still applies underneath as the baseline tempo.
function Write-Ssml([string]$voice, [string]$dir, [string]$culture, [string]$body, [string]$file, [int]$rate) {
    $ssml = @"
<speak version="1.0" xmlns="http://www.w3.org/2001/10/synthesis" xml:lang="$culture">$body</speak>
"@
    $synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
    try {
        $synth.SelectVoice($voice)
        $synth.Rate = $rate
        $synth.Volume = 100
        $synth.SetOutputToWaveFile((Join-Path $dir $file), $fmt)
        $synth.SpeakSsml($ssml)
    } finally {
        # See Write-Line: Dispose does not reliably flush the wave writer on its own.
        $synth.SetOutputToNull()
        $synth.Dispose()
    }
}

function Write-Language([string]$lang) {
    if ($lang -eq "ko") {
        $voice = "Microsoft Heami Desktop"
        $culture = "ko-KR"
        $sub = "Voice\ko"
        # ONE STEP FASTER THAN ENGLISH, by ear (2026-08-21, by request). Heami at -2 read as
        # laboured where Zira at -2 reads as measured - a Korean sentence carries more syllables for
        # the same content, so the same rate spends longer saying it. -1 puts the Korean iteration
        # line at 3.7s against the English 5.5s and gets the delivery back to an announcement.
        $rateSentence = -1
        # And the digits with them. There is headroom: at -1 the longest Korean digit was 1.35s
        # against English's 1.63s, and each has to finish inside a one-second slot.
        $rateDigit = 0
    } else {
        $voice = "Microsoft Zira Desktop"
        $culture = "en-US"
        $sub = "Voice"
        $rateSentence = -2
        $rateDigit = -1
    }

    # Fail loudly rather than silently falling back to whatever voice IS installed - a Korean folder
    # full of an American voice reading Hangul as gibberish is the worst possible outcome here.
    $installed = (New-Object System.Speech.Synthesis.SpeechSynthesizer).GetInstalledVoices() |
                 ForEach-Object { $_.VoiceInfo.Name }
    if ($installed -notcontains $voice) {
        Write-Error "Voice '$voice' is not installed. Available: $($installed -join ', ')"
        return
    }

    $dir = Join-Path $PSScriptRoot "..\Assets\Audio\$sub"
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $dir = (Resolve-Path $dir).Path

    $count = 0

    # "Iteration N, 60 seconds remaining." as one clip per N rather than stitching a number onto a
    # shared tail - the synthesizer gets the sentence intonation right only when it sees the whole
    # sentence. 30 covers far more iterations than a prototype run reaches; past that the generic
    # line below stands in.
    #
    # The number is lifted in pitch and left rising, then a beat before the informational tail - so
    # it lands as "Iteration one!" rather than as a label being read off a list.
    #
    # Note the "?" and that it is NOT a mistake. Getting a rising ending out of SAPI takes a question
    # mark: the terminal contour is chosen from sentence punctuation, and it overrides everything
    # else. Measured on Zira, over the number, at rate -2:
    #     "1!"                      220 -> 160 Hz   falls (declarative terminal, exclamation ignored)
    #     prosody contour="..."     202 -> 138 Hz   worse; SAPI ignores the attribute
    #     "1?"                      182 -> 232 Hz   rises
    #     pitch +35% and "1?"       179 -> 259 Hz   rises furthest        <- this
    # The "?" is never spoken, and because it closes the sentence there, the rise sits on the number.
    # "60 seconds remaining." is then its own sentence and keeps its normal falling ending - putting
    # the rise at the end of the whole line instead would turn "remaining" into a question.
    #
    # The Korean lines are built the same way and the same trick is applied, but whether Heami honours
    # the "?" the way Zira does has NOT been measured - listen before trusting the contour.
    foreach ($n in 1..30) {
        if ($lang -eq "ko") {
            # "ITERATION" IS NOT TRANSLATED, here or anywhere (2026-08-21, by request). It is the
            # game's own word - the title, and the unit the whole loop is counted in - so the PA
            # keeps saying it in both languages, the same way the facility's signage does.
            #
            # Written in Latin rather than as 이터레이션, and the two are interchangeable: Heami
            # transliterates the Latin word internally and both spellings produce byte-identical
            # clip lengths (1.90s alone, 3.66s in the full line at rate -1). It does NOT spell the
            # letters out, which was the risk worth measuring. Latin wins on being readable here.
            $body = "Iteration <prosody pitch=""+35%"">$n</prosody>?<break time=""350ms"" /> 60초 남았습니다."
        } else {
            $body = "Iteration <prosody pitch=""+35%"">$n</prosody>?<break time=""350ms"" /> 60 seconds remaining."
        }
        Write-Ssml $voice $dir $culture $body ("voice_iteration_{0:00}.wav" -f $n) $rateSentence
        $count++
    }

    if ($lang -eq "ko") {
        Write-Ssml $voice $dir $culture "새 <prosody pitch=""+35%"">Iteration</prosody>?<break time=""350ms"" /> 60초 남았습니다." "voice_iteration_generic.wav" $rateSentence
        Write-Line $voice $dir "새 사이클을 시작합니다." "voice_new_cycle.wav" $rateSentence
        # Spoken when the player ends a cycle themselves instead of running the clock out. The
        # distinction matters: a voluntary end skips the countdown entirely, which is otherwise the
        # loop's loudest beat.
        Write-Line $voice $dir "사이클을 종료했습니다." "voice_cycle_terminated.wav" $rateSentence
        # The ending, and the only line a run hears exactly once. Built out of the vocabulary the
        # player already has - cycles are started and terminated all game - so a cycle being *broken*
        # reads as the same voice admitting the machine failed.
        Write-Line $voice $dir "격리 실패. 사이클이 파괴되었습니다." "voice_cycle_broken.wav" $rateSentence
        # "종료" rather than a borrowed English word, because that is what this voice has already
        # called the same act above.
        Write-Line $voice $dir "수동 종료 가능. N 키를 길게 누르십시오." "voice_manual_termination.wav" $rateSentence
        # Room3-2N's cube. The one line in the game that speaks TO the player rather than about the
        # machine - see NarrationDirector.AnnounceAllCyclesBroken for why that is the point of it.
        Write-Line $voice $dir "모든 사이클이 파괴되었습니다. 당신은 사이클을 파괴한 대가를 치르게 될 것입니다." "voice_all_cycles_broken.wav" $rateSentence
    } else {
        Write-Ssml $voice $dir $culture "New <prosody pitch=""+35%"">iteration</prosody>?<break time=""350ms"" /> 60 seconds remaining." "voice_iteration_generic.wav" $rateSentence
        Write-Line $voice $dir "New cycle initialized." "voice_new_cycle.wav" $rateSentence
        Write-Line $voice $dir "Cycle terminated." "voice_cycle_terminated.wav" $rateSentence
        Write-Line $voice $dir "Containment failure. Cycle broken." "voice_cycle_broken.wav" $rateSentence
        Write-Line $voice $dir "Manual termination available. Hold N to end the cycle." "voice_manual_termination.wav" $rateSentence
        Write-Line $voice $dir "All cycles have been destroyed. You will pay the price for destroying them." "voice_all_cycles_broken.wav" $rateSentence
    }
    $count += 6

    if ($lang -eq "ko") { $ten = "10초 남았습니다." } else { $ten = "10 seconds remaining." }
    Write-Line $voice $dir $ten "voice_ten_seconds.wav" $rateDigit
    $count++

    # SINO-KOREAN, NOT NATIVE. A Korean counts down "구, 팔, 칠" the way a clock does and "아홉,
    # 여덟, 일곱" the way a person counting objects does - and this is a machine reading a clock. It
    # is also the shorter of the two by a syllable or more per digit, which matters here: each digit
    # has to finish inside its one-second slot before the next one replaces it.
    if ($lang -eq "ko") {
        $digits = @{ 9 = "구."; 8 = "팔."; 7 = "칠."; 6 = "육."; 5 = "오."
                     4 = "사."; 3 = "삼."; 2 = "이."; 1 = "일." }
    } else {
        $digits = @{ 9 = "Nine."; 8 = "Eight."; 7 = "Seven."; 6 = "Six."; 5 = "Five."
                     4 = "Four."; 3 = "Three."; 2 = "Two."; 1 = "One." }
    }
    foreach ($d in 9..1) {
        Write-Line $voice $dir $digits[$d] ("voice_count_{0}.wav" -f $d) $rateDigit
        $count++
    }

    # ================================================================ THE REPORT, SPOKEN
    #
    # **NUMBERS ARE ASSEMBLED FROM CLIPS, NOT SYNTHESISED PER SENTENCE.** The evaluation the facility
    # reads out at the end of the game contains numbers nobody can know in advance - how many
    # iterations each cycle took and how long - so the line has to be built at runtime out of pieces.
    # See `NarrationDirector.AnnounceCycleResult`.
    #
    # 0-19 are whole words and 20-90 are the tens, which is the split that lets ONE code path serve
    # both languages: English needs it because "thirteen" is not "ten three", and Korean gets it for
    # free because 십삼 assembled from 십 and 삼 sounds like someone spelling rather than speaking.
    # Past twenty both languages are regular, so tens + unit covers 20-99 in either.
    #
    # SINO-KOREAN throughout, for the reason the countdown above already records: this is a machine
    # reading a clock, not a person counting objects.
    if ($lang -eq "ko") {
        $units = @("영", "일", "이", "삼", "사", "오", "육", "칠", "팔", "구",
                   "십", "십일", "십이", "십삼", "십사", "십오", "십육", "십칠", "십팔", "십구")
        $tens  = @("이십", "삼십", "사십", "오십", "육십", "칠십", "팔십", "구십")
        $words = @{ "cycle" = "사이클"; "iterations" = "회 반복"; "minutes" = "분"; "total" = "합계" }
    } else {
        $units = @("Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine",
                   "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen",
                   "Seventeen", "Eighteen", "Nineteen")
        $tens  = @("Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety")
        $words = @{ "cycle" = "Cycle"; "iterations" = "iterations"; "minutes" = "minutes"
                    "total" = "Total" }
    }

    # NO FULL STOP ON ANY OF THESE. A number in the middle of an assembled sentence is not the end of
    # one, and the synthesizer drops its pitch and pauses on a period - which is what turns a read-out
    # into a list of separate announcements. The gap between clips is `NarrationDirector.clipGap`.
    for ($i = 0; $i -lt $units.Length; $i++) {
        Write-Line $voice $dir $units[$i] ("voice_num_{0:00}.wav" -f $i) $rateDigit
        $count++
    }
    for ($i = 0; $i -lt $tens.Length; $i++) {
        Write-Line $voice $dir $tens[$i] ("voice_num_{0:00}.wav" -f (($i + 2) * 10)) $rateDigit
        $count++
    }
    # **THE ONE SPOKEN SENTENCE THE ENDING ADDS.** Said once the report has been read out, so the
    # player standing in a wrecked room with nothing to do knows that something is coming and that
    # waiting is the right thing to be doing. Everything else this voice says at the end is a number.
    if ($lang -eq "ko") {
        $transport = "이송 수단을 호출했습니다. 잠시만 기다려 주십시오."
    } else {
        $transport = "Transport has been called. Please stand by."
    }
    Write-Line $voice $dir $transport "voice_transport_called.wav" $rateSentence
    $count++

    foreach ($key in $words.Keys) {
        Write-Line $voice $dir $words[$key] ("voice_word_{0}.wav" -f $key) $rateDigit
        $count++
    }

    Write-Host "Wrote $count $lang clips to $dir"
}

if ($Language -eq "both") {
    Write-Language "en"
    Write-Language "ko"
} else {
    Write-Language $Language
}
