module ClientFableTests

open Fable.Mocha
open Eel.Client.Model
open Eel.Client.GameLoop
open Shared

module Game = Shared.Game

let private mkLetterModel () =
    let head = { X = 5; Y = 5 }
    let token0 =
        { LetterIndex = 0
          Position = { X = head.X + 1; Y = head.Y }
          Status = FoodStatus.Active }

    let token1 =
        { LetterIndex = 1
          Position = { X = head.X; Y = head.Y + 1 }
          Status = FoodStatus.Active }

    let game =
        { Game.initialState () with
            Eel = [ head ]
            Direction = Direction.Right
            Foods = [ token0; token1 ]
            Score = 0
            GameOver = false }

    { initModel with
        Game = game
        GameRunning = true
        TargetText = "ok"
        TargetIndex = 0 }
    |> ensureFoodsForModel

let loopTests =
    testList "Game loop" [
        testCase "collecting food advances target text" <| fun _ ->
            let result = applyTick (mkLetterModel ())

            Expect.equal result.Model.TargetIndex 1 "Should increment target index"
            Expect.isFalse (List.isEmpty result.Effects) "Expect at least a high-score persistence effect"
            Expect.isFalse (result.Effects |> List.contains StopLoop) "Loop keeps running"

        testCase "finishing phrase schedules countdown" <| fun _ ->
            let baseModel = mkLetterModel ()
            let collectedPos = { X = 4; Y = 5 }
            let activePos = { X = 6; Y = 5 }

            let progressed =
                { baseModel with
                    TargetIndex = 1
                    Game =
                        { baseModel.Game with
                            Score = 10
                            Foods =
                                [ { LetterIndex = 0
                                    Position = collectedPos
                                    Status = FoodStatus.Collected }
                                  { LetterIndex = 1
                                    Position = activePos
                                    Status = FoodStatus.Active } ] } }

            let result = applyTick progressed

            Expect.equal result.Model.TargetText "" "Phrase resets"
            Expect.equal result.Model.TargetIndex 0 "Target index resets"
            Expect.isTrue (result.Effects |> List.contains StopLoop) "Loop stops after phrase completion"
            Expect.isTrue (result.Effects |> List.contains ScheduleCountdown) "Countdown restarts for next round"
            Expect.isTrue (result.Effects |> List.contains FetchVocabulary) "Next vocabulary request scheduled"
    ]

Mocha.runTests loopTests |> ignore
