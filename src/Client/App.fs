module Eel.Client.App

open Elmish
open Elmish.React
open Eel.Client.Model
open Eel.Client.Update
open Eel.Client.View

Program.mkProgram init update view
|> Program.withReactSynchronous "app"
|> Program.run
