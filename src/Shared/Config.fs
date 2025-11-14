namespace Shared

type FoodBurstConfig =
    { Enabled: bool
      MaxConcurrentWaves: int option
      WaveSpeedSegmentsPerMs: float
      LetterSizeFactor: float
      LetterWeightFactor: float
      SegmentWeightFactor: float }

type BoardVisualConfig =
    { CellBaseColor: string
      CellHighlightOpacity: float
      LetterColor: string
      LetterAlpha: float
      LetterSize: float }

type FoodVisualConfig =
    { ShowLetters: bool
      ActiveFill: string
      CollectedFill: string }

type GameplayConfig =
    { InitialSpeedMs: int
      MaxVisibleFoods: int
      StartCountdownMs: int
      LevelCountdownMs: int
      CelebrationDelayMs: int
      RotateSegmentLetters: bool
      FoodBurst: FoodBurstConfig
      BoardVisuals: BoardVisualConfig
      FoodVisuals: FoodVisualConfig }

module Config =
    let gameplay : GameplayConfig =
        { InitialSpeedMs = 200
          MaxVisibleFoods = 20
          StartCountdownMs = 3000
          LevelCountdownMs = 1500
          CelebrationDelayMs = 1200
          RotateSegmentLetters = true
          FoodBurst =
            { Enabled = true
              MaxConcurrentWaves = Some 3
              WaveSpeedSegmentsPerMs = 0.001
              LetterSizeFactor = 1.5
              LetterWeightFactor = 1.3
              SegmentWeightFactor = 0.8 }
          BoardVisuals =
            { CellBaseColor = "rgba(255,255,255,0.04)"
              CellHighlightOpacity = 0.15
              LetterColor = "#6a7272ff"
              LetterAlpha = 0.5
              LetterSize = 14.0 }
          FoodVisuals =
            { ShowLetters = false
              ActiveFill = "rgba(88,161,107,0.8)"
              CollectedFill = "rgba(255,255,255,0.15)" } }
