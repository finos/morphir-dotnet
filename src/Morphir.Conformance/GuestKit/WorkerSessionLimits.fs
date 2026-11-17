// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Conformance.GuestKit

open System

// WorkerSessionLimits is alone in its file. F# initialises the class `static let` values of a file in one
// initialiser, which starts the type initialiser of each class whose fields it writes; with a second class or a
// module value in the file, two threads could wait for each other and the CLR would let one of them see Default
// before it is set (FailStaticInit). Alone, the file is entered only through this type.

/// How long the sessions of a WorkerSessions factory may take.
[<Sealed>]
type WorkerSessionLimits private (sessionLimit: TimeSpan, exitWait: TimeSpan) =
    /// The longest span Task.Delay takes: 4294967294 ms, about 49.7 days.
    static let longest = TimeSpan.FromMilliseconds 4294967294.0

    static let defaults =
        WorkerSessionLimits(TimeSpan.FromMinutes 10.0, TimeSpan.FromSeconds 10.0)

    static let check (name: string) (what: string) (span: TimeSpan) =
        if span <= TimeSpan.Zero || span > longest then
            raise (
                ArgumentOutOfRangeException(
                    name,
                    span,
                    $"The {what} must be positive and at most 4294967294 ms, the longest span Task.Delay takes."
                )
            )

    /// The defaults: a worker may run 10 minutes, which is more than one call deadline of extism-kernel-v1 for each
    /// request of a model-tier session, and EndAsync waits 10 seconds for it to exit, as the model-tier runner waits
    /// for a session to end after morphir.exit. Every read returns the same instance.
    static member Default = defaults

    /// Limits with these spans. Each must be positive and at most 4294967294 ms, the longest span Task.Delay takes;
    /// otherwise this throws ArgumentOutOfRangeException.
    static member Create(sessionLimit: TimeSpan, exitWait: TimeSpan) =
        check (nameof sessionLimit) "session limit" sessionLimit
        check (nameof exitWait) "exit wait" exitWait
        WorkerSessionLimits(sessionLimit, exitWait)

    /// How long a worker may run from its start. A worker still running then has its process tree stopped, which
    /// ends its session: every request still waiting gets an error.
    member _.SessionLimit = sessionLimit

    /// How long EndAsync waits for a worker to exit by itself before it stops the worker's process tree.
    member _.ExitWait = exitWait

    /// Stable text for diagnostics.
    override _.ToString() = $"session limit {sessionLimit}, exit wait {exitWait}"
