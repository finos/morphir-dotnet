namespace Morphir.Internal.CodeGeneration.Tests.TestData

open Morphir.Internal.CodeGeneration

/// Test record for lens generation
[<GenerateLenses>]
type Config = {
    Port: int
    Host: string
    Timeout: int
    Enabled: bool
}

/// Test record with nested types
[<GenerateLenses>]
type ServerConfig = {
    Config: Config
    MaxConnections: int
    LogLevel: string
}

