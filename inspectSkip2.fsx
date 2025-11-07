#r "nuget: xunit.extensibility.execution, 2.9.2"
open Xunit.Sdk
printfn "%A" (typeof<TestSkippedException>.GetConstructors())
