using FluentValidation;

namespace Morphir.Tooling.Features.Management;

/// <summary>Explicit registration of a named local developer repository.</summary>
public sealed record ExtensionRepositoryAddLocal(string Name, string Directory, string Home, bool LocalDeveloper, bool DryRun);
/// <summary>Local declaration mode within an already registered repository. It never downloads or probes.</summary>
public sealed record ExtensionLocalDeclarationInstall(string Name, string Repository, string Release, string Archive,
    string Home, bool LocalDeveloper, bool NoProbe, bool DryRun);
/// <summary>Installation from a provider source such as <c>github:owner/repo@1.2.3</c>. It never probes.</summary>
public sealed record ExtensionSourceInstall(string Source, string Home, bool NoProbe, bool DryRun);
/// <summary>A command outcome, not an installation receipt or authentication statement.</summary>
public sealed record ExtensionLocalOutcome(bool Success, string Code, string Message);

// ===== Extension Install Command =====

public record ExtensionInstall(
    string Name,
    string SourceUrl,
    string Version,
    string? Platform = null,
    bool Local = false
);

public record ExtensionInstallResult(
    bool Success,
    string Name,
    string Version,
    string Platform,
    string InstalledPath,
    string? ErrorMessage = null
);

// ===== Extension Use Command =====

public record ExtensionUse(
    string Name,
    string Version,
    string? Platform = null,
    bool Local = false
);

public record ExtensionUseResult(
    bool Success,
    string Name,
    string Version,
    string Platform,
    bool IsLocal,
    string? ErrorMessage = null
);

// ===== Extension Remove Command =====

public record ExtensionRemove(
    string Name,
    string Version,
    string? Platform = null,
    bool Local = false
);

public record ExtensionRemoveResult(
    bool Success,
    string Name,
    string Version,
    string Platform,
    bool IsLocal,
    string? ErrorMessage = null
);

// ===== Extension Which Command =====

public record ExtensionWhich(
    string Name,
    string? Platform = null
);

public record ExtensionWhichResult(
    bool Found,
    string Name,
    string? Version,
    string? Platform,
    string? Path,
    bool IsLocal,
    string? ErrorMessage = null
);

// ===== Validators =====

public class ExtensionInstallValidator : AbstractValidator<ExtensionInstall>
{
    public ExtensionInstallValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Extension name is required");

        RuleFor(x => x.SourceUrl)
            .NotEmpty()
            .WithMessage("Source URL is required")
            .Must(uri => Uri.TryCreate(uri, UriKind.Absolute, out _))
            .WithMessage("Source URL must be a valid absolute URI");

        RuleFor(x => x.Version)
            .NotEmpty()
            .WithMessage("Version is required");

        RuleFor(x => x.Platform)
            .Must((cmd, platform) => platform == null || Infrastructure.RuntimeIdentifier.IsValidRid(platform))
            .WithMessage("Platform must be a valid Runtime Identifier (e.g., linux-x64, win-x64, osx-arm64)");
    }
}

public class ExtensionUseValidator : AbstractValidator<ExtensionUse>
{
    public ExtensionUseValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Extension name is required");

        RuleFor(x => x.Version)
            .NotEmpty()
            .WithMessage("Version is required");

        RuleFor(x => x.Platform)
            .Must((cmd, platform) => platform == null || Infrastructure.RuntimeIdentifier.IsValidRid(platform))
            .WithMessage("Platform must be a valid Runtime Identifier (e.g., linux-x64, win-x64, osx-arm64)");
    }
}

public class ExtensionRemoveValidator : AbstractValidator<ExtensionRemove>
{
    public ExtensionRemoveValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Extension name is required");

        RuleFor(x => x.Version)
            .NotEmpty()
            .WithMessage("Version is required");

        RuleFor(x => x.Platform)
            .Must((cmd, platform) => platform == null || Infrastructure.RuntimeIdentifier.IsValidRid(platform))
            .WithMessage("Platform must be a valid Runtime Identifier (e.g., linux-x64, win-x64, osx-arm64)");
    }
}

public class ExtensionWhichValidator : AbstractValidator<ExtensionWhich>
{
    public ExtensionWhichValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Extension name is required");

        RuleFor(x => x.Platform)
            .Must((cmd, platform) => platform == null || Infrastructure.RuntimeIdentifier.IsValidRid(platform))
            .WithMessage("Platform must be a valid Runtime Identifier (e.g., linux-x64, win-x64, osx-arm64)");
    }
}
