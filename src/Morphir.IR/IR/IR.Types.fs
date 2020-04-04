namespace Morphir.IR

type IHaveAttribution<'A> =
   abstract member Attributes:'A with get

type Expression<'A> =
     inherit IHaveAttribution<'A>
