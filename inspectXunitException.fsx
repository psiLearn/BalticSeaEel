#r "nuget: xunit.assert, 2.9.2"
open Xunit.Sdk
printfn "%A" (typeof<XunitException>.GetConstructors())
