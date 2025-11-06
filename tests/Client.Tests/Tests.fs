module Tests

open Xunit
open Elmish
open Eel.Client.Model
open Shared

module Game = Shared.Game
module Update = Eel.Client.Update

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
