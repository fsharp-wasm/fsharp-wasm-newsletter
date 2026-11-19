/// The .NET run of the suites: TAP on standard output, exit code = number of failures.  (The WasmGC run is Main.fs.)
module Newsletter.TestProgram

[<EntryPoint>]
let main _ = if Wasmecto.Runner.runAll (fun line -> printfn "%s" line) Newsletter.Suites.all = 0 then 0 else 1
