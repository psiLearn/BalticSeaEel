module ClientFableTests

open Fable.Mocha
open Eel.Client.Model
open Eel.Client.GameLoop
open Shared

module Game = Shared.Game

let private mkLetterModel () =
    let head = { X = 5; Y = 5 }

    let game =
        { Game.initialState () with
            Eel = [ head ]
            Direction = Direction.Right
            Food = { X = head.X + 1; Y = head.Y }
            Score = 0
            GameOver = false }

    { initModel with
        Game = game
        GameRunning = true
        TargetText = "ok"
        TargetIndex = 0 }

let loopTests =
    testList "Game loop" [
        testCase "collecting food advances target text" <| fun _ ->
            let result = applyTick (mkLetterModel ())

            Expect.equal result.Model.TargetIndex 1 "Should increment target index"
            Expect.isFalse (List.isEmpty result.Effects) "Expect at least a high-score persistence effect"
            Expect.isFalse (result.Effects |> List.contains StopLoop) "Loop keeps running"

        testCase "finishing phrase schedules countdown" <| fun _ ->
            let model = mkLetterModel ()
            let progressed = { model with TargetIndex = 1 }

            let result = applyTick progressed

            Expect.equal result.Model.TargetText "" "Phrase resets"
            Expect.equal result.Model.TargetIndex 0 "Target index resets"
            Expect.isTrue (result.Effects |> List.contains StopLoop) "Loop stops after phrase completion"
            Expect.isTrue (result.Effects |> List.contains ScheduleCountdown) "Countdown restarts for next round"
            Expect.isTrue (result.Effects |> List.contains FetchVocabulary) "Next vocabulary request scheduled"
    ]

Mocha.runTests loopTests |> ignore
