namespace Morphir.CLI.Services

open Microsoft.Extensions.Logging
open Microsoft.Extensions.Hosting

type InfoService(hostApplicationLifetime: IHostApplicationLifetime, logger: ILogger<InfoService>) =
    inherit BackgroundService()

    override this.ExecuteAsync(stoppingToken) = task {
        logger.LogInformation("InfoService is running.")
        hostApplicationLifetime.StopApplication()
    }
