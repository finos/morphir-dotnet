namespace Morphir.Extensions.Engine

type Tool =
    abstract member Run: unit -> int

type Project =
    abstract member Name: string
    abstract member Tools: Tool list
    abstract member Build: unit -> Async<int>

type Workspace =
    abstract member Tools: Tool list
    abstract member Projects: Project list
    abstract member Build: unit -> Async<int>
    abstract member Test: unit -> Async<int>
