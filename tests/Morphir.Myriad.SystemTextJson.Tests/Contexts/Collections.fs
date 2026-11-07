// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Myriad.SystemTextJson.Tests.Collections

open System.Collections.Generic

/// One field of each collection shape, with the context default map format.
type Bag =
    {
        Maybe: int option
        MaybeValue: string voption
        Items: int list
        Words: string[]
        Tags: Set<string>
        Buffer: ResizeArray<int>
        ReadOnly: IReadOnlyList<int>
        Collection: IReadOnlyCollection<string>
        Pair: int * string
        Nested: int list list
        Lookup: Map<string, int>
        ByNumber: Map<int, string>
        Dictionary: IReadOnlyDictionary<string, bool>
    }

/// A collection type with a name, so the context has a member for it.
type Bags = Bag list
