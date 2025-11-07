#r "nuget: xunit, 2.9.2"
open Xunit.Sdk
printfn "%A" (typeof<SkipException>.GetConstructors())
