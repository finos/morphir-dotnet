// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Knowledge

open System

/// The identity of an individual annotation.
type AnnotationId =
    private
    | AnnotationId of Iri

    /// The annotation IRI.
    member this.Iri =
        let (AnnotationId iri) = this
        iri

    override this.ToString() = this.Iri.Value

    /// Wraps a parsed absolute IRI.
    static member Create(iri: Iri) =
        ArgumentNullException.ThrowIfNull iri
        AnnotationId iri

/// The identity of an individual directional link.
type LinkId =
    private
    | LinkId of Iri

    /// The link IRI.
    member this.Iri =
        let (LinkId iri) = this
        iri

    override this.ToString() = this.Iri.Value

    /// Wraps a parsed absolute IRI.
    static member Create(iri: Iri) =
        ArgumentNullException.ThrowIfNull iri
        LinkId iri

/// Author and creation time recorded for an annotation or a claim.
type AnnotationProvenance =
    {
        /// The agent who created the annotation or claim.
        Author: Iri
        /// The instant when the annotation or claim was created.
        CreatedAt: DateTimeOffset
    }

    override this.ToString() = $"{this.Author} @ {this.CreatedAt:O}"

/// A typed body attached to a typed knowledge target.
type Annotation<'Target, 'Body> =
    {
        /// The independent identity of the annotation.
        Id: AnnotationId
        /// The selected text, graph, claim or other target.
        Target: 'Target
        /// The consumer's typed payload.
        Body: 'Body
        /// The author and creation time.
        Provenance: AnnotationProvenance
    }

    override this.ToString() = this.Id.ToString()

/// A directional relationship with typed endpoints.
type KnowledgeLink<'Source, 'Target> =
    {
        /// The independent identity of the link.
        Id: LinkId
        /// The endpoint where the relationship begins.
        Source: 'Source
        /// The meaning of the relationship.
        Predicate: Iri
        /// The endpoint where the relationship ends.
        Target: 'Target
    }

    override this.ToString() = this.Id.ToString()
