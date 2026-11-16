// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Lang.CSharp

open System
open System.IO
open System.Reflection

/// <summary>
/// The reference assemblies that the package embeds. The Native AOT host has no assembly files on disk, so the frontend
/// reads every reference from its own resources.
/// </summary>
module internal Embedded =

    let private assembly = Assembly.GetExecutingAssembly()

    /// <summary>The bytes of an embedded resource, or None when the resource is absent.</summary>
    let tryRead (name: string) : byte[] option =
        match assembly.GetManifestResourceStream name with
        | null -> None
        | stream ->
            use stream = stream
            use memory = new MemoryStream()
            stream.CopyTo memory
            Some(memory.ToArray())
