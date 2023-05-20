module Morphir.Myriad.Plugins.Debugging

open System
let mutable shouldBreak = true

let waitForDebuggerAttached (programName) =
#if DEBUG
    if not (Diagnostics.Debugger.IsAttached) then
        printfn
            "Please attach a debugger for %s, PID: %d"
            programName
            (Diagnostics.Process.GetCurrentProcess().Id)

    while not (Diagnostics.Debugger.IsAttached) do
        System.Threading.Thread.Sleep(100)

    if shouldBreak then
        Diagnostics.Debugger.Break()
        shouldBreak <- false
#else
    ()
#endif
