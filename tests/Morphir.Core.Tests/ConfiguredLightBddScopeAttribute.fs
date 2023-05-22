namespace Morphir.IR.Tests

open LightBDD.Core.Configuration
open LightBDD.Framework.Configuration
open LightBDD.Framework.Reporting.Formatters
open LightBDD.XUnit2

type ConfiguredLightBddScopeAttribute() =
    inherit LightBddScopeAttribute()

    override _.OnConfigure(configuration: LightBddConfiguration) : unit =
        configuration
            .ReportWritersConfiguration()
            .AddFileWriter<PlainTextReportFormatter>("~/Reports/FeaturesReport.txt")
        |> ignore
