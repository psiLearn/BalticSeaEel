namespace Shared

type GameplayConfig =
    { InitialSpeedMs: int
      MaxVisibleFoods: int }

module Config =
    let gameplay : GameplayConfig =
        { InitialSpeedMs = 200
          MaxVisibleFoods = 10 }
