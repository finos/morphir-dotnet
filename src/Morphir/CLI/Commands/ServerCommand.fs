namespace Morphir.CLI.Commands

open Oakton
open Microsoft.FSharp.Control
open Serilog


type ServerInput() =
    inherit NetCoreInput()

[<Description("Run the Morphir CLI tooling in server mode.")>]
type ServerCommand() =
    inherit OaktonAsyncCommand<ServerInput>()

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
