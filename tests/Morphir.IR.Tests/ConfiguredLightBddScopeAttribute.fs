namespace Morphir.IR.Tests

open LightBDD.Framework.Configuration
open LightBDD.XUnit2

type ConfiguredLightBddScopeAttribute() =
    inherit LightBddScopeAttribute()
