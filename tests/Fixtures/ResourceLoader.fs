namespace Morphir.Testing

open System.IO
open System.Text.Json.Nodes

type private ResourceAnchor = ResourceAnchor

/// Loads test data linked into the calling test project's assembly.
module ModelResources =
    let read name =
        let resource =
            "Morphir.Tests.Fixtures."
            + name

        use stream = typeof<ResourceAnchor>.Assembly.GetManifestResourceStream(resource)

        if isNull stream then
            invalidArg (nameof name) $"Test resource '{resource}' was not found."

        use reader = new StreamReader(stream)
        reader.ReadToEnd()

    /// Normalizes formatting for tests that mutate a JSON token in a fixture.
    let json name =
        JsonNode.Parse(read name).ToJsonString()
