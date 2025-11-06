module Tests

open Xunit
open Eel.Client.Model
open Shared

module Game = Shared.Game
module Update = Eel.Client.Update
module Loop = Eel.Client.GameLoop

let private withTarget target index =
    { initModel with TargetText = target; TargetIndex = index }

[<Fact>]
let ``nextTargetChar returns current character when index is in range`` () =
    let model = withTarget "Eel" 1
    let actual = nextTargetChar model
    Assert.Equal<char option>(Some 'e', actual)

[<Fact>]
let ``nextTargetChar returns none when phrase is completed`` () =
    let model = withTarget "Eel" 3
    let actual = nextTargetChar model
    Assert.Equal<char option>(None, actual)

[<Fact>]
let ``progressParts splits built and remaining segments`` () =
    let model = withTarget "abc" 2
    let built, remaining = progressParts model
    Assert.Equal("ab", built)
    Assert.Equal("c", remaining)

[<Fact>]
let ``progressParts clamps built portion to phrase length`` () =
    let model = withTarget "snack" 99
    let built, remaining = progressParts model
    Assert.Equal("snack", built)
    Assert.Equal("", remaining)

[<Fact>]
let ``changeDirection ignores opposite direction`` () =
    let state =
        { Game.initialState () with
            Direction = Direction.Left }

    let updated = Game.changeDirection Direction.Right state
    Assert.Equal(Direction.Left, updated.Direction)

[<Fact>]
let ``move marks game over when head crosses board boundary`` () =
    let state =
        { Game.initialState () with
            Eel = [ { X = Game.boardWidth - 1; Y = 3 } ]
            Direction = Direction.Right
            Food = { X = 0; Y = 0 }
            Score = 0
            GameOver = false }

    let next = Game.move state
    Assert.True(next.GameOver)

[<Fact>]
let ``move grows eel and updates score when food is collected`` () =
    let head = { X = 5; Y = 5 }

    let state =
        { Game.initialState () with
            Eel = [ head ]
            Direction = Direction.Right
            Food = { X = head.X + 1; Y = head.Y }
            Score = 0
            GameOver = false }

    let next = Game.move state

    Assert.False(next.GameOver)
    Assert.Equal(10, next.Score)
    Assert.Equal(head.X + 1, next.Eel.Head.X)
    Assert.Equal(2, next.Eel.Length)

[<Fact>]
let ``update Tick stops loop when game already over`` () =
    let game =
        { Game.initialState () with
            Score = 20
            GameOver = true }

    let model =
        { initModel with
            Game = game
            GameRunning = true
            HighScore = Some { Name = "A"; Score = 999 } }

    let updated, cmd = Update.update Tick model

    Assert.False(updated.GameRunning)
    Assert.Equal(game, updated.Game)
    Assert.NotEmpty(cmd)

[<Fact>]
let ``update Tick advances target when collecting letter`` () =
    let head = { X = 5; Y = 5 }

    let modelGame =
        { Eel = [ head ]
          Direction = Direction.Right
          Food = { X = head.X + 1; Y = head.Y }
          Score = 0
          GameOver = false }

    let model =
        { initModel with
            Game = modelGame
            GameRunning = true
            HighScore = Some { Name = "A"; Score = 999 }
            TargetText = "ok"
            TargetIndex = 0 }

    let updated, cmd = Update.update Tick model

    Assert.True(updated.GameRunning)
    Assert.Equal(1, updated.TargetIndex)
    Assert.Equal("ok", updated.TargetText)
    Assert.Empty(cmd)

[<Fact>]
let ``applyTick marks game over and requests loop stop`` () =
    let game =
        { Game.initialState () with
            Score = 30
            GameOver = true }

    let model =
        { initModel with
            Game = game
            GameRunning = true
            HighScore = Some { Name = "A"; Score = 25 } }

    let result = Loop.applyTick model

    Assert.False(result.Model.GameRunning)
    Assert.Contains(Loop.GameOver, result.Events)
    Assert.Contains(Loop.StopLoop, result.Effects)

[<Fact>]
let ``applyTick completes phrase and prepares next round`` () =
    let head = { X = 2; Y = 2 }

    let modelGame =
        { Eel = [ head ]
          Direction = Direction.Right
          Food = { X = head.X + 1; Y = head.Y }
          Score = 10
          GameOver = false }

    let model =
        { initModel with
            Game = modelGame
            GameRunning = true
            TargetText = "hi"
            TargetIndex = 1
            SpeedMs = 200
            UseExampleNext = false }

    let result = Loop.applyTick model

    Assert.False(result.Model.GameRunning)
    Assert.Equal("", result.Model.TargetText)
    Assert.Equal(0, result.Model.TargetIndex)
    Assert.Equal(3000, result.Model.CountdownMs)
    Assert.True(result.Model.UseExampleNext)
    Assert.Contains(Loop.CleanupLoop, result.Effects)
    Assert.Contains(Loop.FetchVocabulary, result.Effects)
    Assert.Contains(Loop.ScheduleCountdown, result.Effects)
    Assert.Contains(Loop.StopLoop, result.Effects)

    let speed =
        match result.Events with
        | [ Loop.PhraseCompleted value ] -> value
        | _ -> failwith "Expected phrase completed event"

    Assert.Equal(result.Model.SpeedMs, speed)

[<Fact>]
let ``applyTick emits high score persistence when score improves`` () =
    let head = { X = 1; Y = 1 }

    let modelGame =
        { Eel = [ head ]
          Direction = Direction.Right
          Food = { X = head.X + 1; Y = head.Y }
          Score = 10
          GameOver = false }

    let model =
        { initModel with
            Game = modelGame
            GameRunning = true
            PlayerName = "  Finn  "
            HighScore = Some { Name = "A"; Score = 5 } }

    let result = Loop.applyTick model

    let expected = { Name = "A"; Score = 20 }

    match result.Effects |> List.tryFind (function | Loop.PersistHighScore _ -> true | _ -> false) with
    | Some (Loop.PersistHighScore (Some hs)) ->
        Assert.Equal(expected.Score, hs.Score)
    | _ -> failwith "Expected PersistHighScore effect"
