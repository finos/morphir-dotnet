namespace Morphir.CLI.Commands

open Oakton
open Microsoft.FSharp.Control
open Microsoft.Extensions.Logging
open Wolverine
open Serilog

type InfoInput() =
    inherit NetCoreInput()

[<Description("Get information about the executing version of the morphir CLI tooling.")>]
type InfoCommand() =
    inherit OaktonAsyncCommand<InfoInput>()

    override this.Execute(input) = task {
        let host = input.BuildHost()
        do! host.StartAsync()
        Log.Information("Executing info command with input: {input}", input)

        Log.Information(
            "User Home Directory: {userHomeDirectory}",
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile)
        )

        Log.Information(
            "User Personal Directory: {userPersonalDirectory}",
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal)
        )
        //Log the User's Application Data folder
        Log.Information(
            "User Application Data Directory: {userApplicationDataDirectory}",
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData)
        )
        // Log the User's Common Application Data Folder
        Log.Information(
            "User Common Application Data Directory: {userCommonApplicationDataDirectory}",
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.CommonApplicationData)
        )
        // Log the User's Local Application Data Folder
        Log.Information(
            "User Local Application Data Directory: {userLocalApplicationDataDirectory}",
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData)
        )

        do! host.StopAsync()
        return true
    }
