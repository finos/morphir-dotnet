namespace Morphir.Files

open System
open System.IO
open System.Runtime.InteropServices
open System.Threading

/// <summary>
/// Replaces a file while another process may hold the destination open for a short time.
/// </summary>
/// <remarks>
/// On Windows a virus scanner or a search indexer opens a file just after it is written, without delete sharing.
/// While it holds the file, Windows refuses to replace it: <c>File.Replace</c> fails with a sharing violation or
/// ERROR_UNABLE_TO_REMOVE_REPLACED, and an overwriting <c>File.Move</c> fails with ERROR_ACCESS_DENIED. Each refusal
/// leaves both files unchanged, so the replacement can wait and try again. The wait gives other writers time to
/// change the files, so the caller checks again what its contract requires before every further attempt. C# and
/// F# callers use the same members.
/// </remarks>
[<AbstractClass; Sealed>]
type FileReplacement =
    /// <summary>The number of attempts. The waits between them (20 ms, doubling up to 500 ms) add up to about
    /// 2.6 seconds.</summary>
    static member Attempts = 10

    /// <summary>
    /// True when <paramref name="error"/> is a failure on Windows that another handle on one of the files causes
    /// and that leaves both files unchanged: a sharing or lock violation (ERROR_SHARING_VIOLATION,
    /// ERROR_LOCK_VIOLATION), ReplaceFile's ERROR_UNABLE_TO_REMOVE_REPLACED, or the access denial
    /// (ERROR_ACCESS_DENIED) of an overwriting move onto a file that is open. ERROR_UNABLE_TO_MOVE_REPLACEMENT and
    /// its second form change the files, so they are not such failures. Always false on other systems, which
    /// replace a file that another handle holds open.
    /// </summary>
    /// <param name="error">The failure of a replacement</param>
    static member IsHeldByAnotherHandle(error: exn) : bool =
        let win32 (hresult: int) = hresult &&& 0xFFFF0000 = 0x80070000
        let code (hresult: int) = hresult &&& 0xFFFF

        RuntimeInformation.IsOSPlatform OSPlatform.Windows
        && (match error with
            | :? UnauthorizedAccessException as denied -> win32 denied.HResult && code denied.HResult = 5
            | :? FileNotFoundException
            | :? DirectoryNotFoundException -> false
            | :? IOException as failure ->
                win32 failure.HResult && (let c = code failure.HResult in c = 32 || c = 33 || c = 1175)
            | _ -> false)

    /// <summary>
    /// Runs <paramref name="replace"/>. While it fails with an error for which
    /// <see cref="IsHeldByAnotherHandle"/> is true, waits and tries again, up to <see cref="Attempts"/> times.
    /// Before each further attempt it calls <paramref name="unchanged"/>, and false stops without replacing. After
    /// the last attempt, the error is raised.
    /// </summary>
    /// <param name="replace">One attempt to replace the file</param>
    /// <param name="unchanged">After a wait: whether the replacement may still go ahead</param>
    /// <returns>True when the file was replaced; false when <paramref name="unchanged"/> returned false.</returns>
    static member Replace(replace: Action, unchanged: Func<bool>) : bool =
        let rec attempt (number: int) =
            let held =
                try
                    replace.Invoke()
                    false
                with error when number < FileReplacement.Attempts && FileReplacement.IsHeldByAnotherHandle error ->
                    true

            if not held then
                true
            else
                Thread.Sleep(min (20 <<< (number - 1)) 500)

                if unchanged.Invoke() then attempt (number + 1) else false

        attempt 1
