namespace Morphir.Extensions.Engine

type Content =
    | Text of string
    | Binary of byte[]
    | Reader of System.IO.StreamReader

type Source = { Name: string; Content: Content }

type FrontendContext = {
    Source: Source
    Options: Map<string, string>
}

type TransformationContext = { Options: Map<string, string> }

type BackendContext = { Options: Map<string, string> }

type IStage<'In, 'Out> =
    abstract member Run: input: 'In -> 'Out

type IFrontendStage<'Out> =
    inherit IStage<FrontendContext, 'Out>

type IBackendStage<'Out> =
    inherit IStage<BackendContext, 'Out>

type ITransformationStage<'Out> =
    inherit IStage<TransformationContext, 'Out>

type ProcessorStages<'Out> = {
    Frontend: IFrontendStage<'Out>
    Transformations: ITransformationStage<'Out> list
    Backend: IBackendStage<'Out>
}

type Processor<'Out>(stages: ProcessorStages<'Out>) =
    static member Create(stages: ProcessorStages<'Out>) = ()

module Processor =
    let create<'Out> (stages: ProcessorStages<'Out>) = Processor<'Out>.Create(stages)
