namespace Shared

type GameplayConfig =
    { InitialSpeedMs: int
      MaxVisibleFoods: int
      StartCountdownMs: int
      LevelCountdownMs: int
      RotateSegmentLetters: bool }

module Config =
    let gameplay : GameplayConfig =
        { InitialSpeedMs = 200
          MaxVisibleFoods = 10
          StartCountdownMs = 5000
          LevelCountdownMs = 3000
          RotateSegmentLetters = true }
