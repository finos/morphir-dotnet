using FluentValidation;

namespace Morphir.Tooling.Features.Management;

// ===== Dist List Command =====

public record DistList(
    string? Platform = null,
    bool Local = false
);

public record DistListResult(
    List<DistInfo> Distributions,
    string Platform,
    bool IsLocal
);

public record DistInfo(
    string Version,
    string Platform,
    string? Description,
    bool IsActive,
    DateTime? InstalledAt
);

// ===== Dist Install Command =====

public record DistInstall(
    string SourceUrl,
    string Version,
    string? Platform = null,
    bool Local = false
);

public record DistInstallResult(
    bool Success,
    string Version,
    string Platform,
    string InstalledPath,
    string? ErrorMessage = null
);

// ===== Dist Use Command =====

public record DistUse(
    string Version,
    string? Platform = null,
    bool Local = false
);

public record DistUseResult(
    bool Success,
    string Version,
    string Platform,
    bool IsLocal,
    string? ErrorMessage = null
);

// ===== Dist Remove Command =====

public record DistRemove(
    string Version,
    string? Platform = null,
    bool Local = false
);

public record DistRemoveResult(
    bool Success,
    string Version,
    string Platform,
    bool IsLocal,
    string? ErrorMessage = null
);

// ===== Dist Which Command =====

public record DistWhich(
    string? Platform = null
);

public record DistWhichResult(
    bool Found,
    string? Version,
    string? Platform,
    string? Path,
    bool IsLocal,
    string? ErrorMessage = null
);

// ===== Validators =====

public class DistInstallValidator : AbstractValidator<DistInstall>
{
    public DistInstallValidator()
    {
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

public class DistUseValidator : AbstractValidator<DistUse>
{
    public DistUseValidator()
    {
        RuleFor(x => x.Version)
            .NotEmpty()
            .WithMessage("Version is required");

        RuleFor(x => x.Platform)
            .Must((cmd, platform) => platform == null || Infrastructure.RuntimeIdentifier.IsValidRid(platform))
            .WithMessage("Platform must be a valid Runtime Identifier (e.g., linux-x64, win-x64, osx-arm64)");
    }
}

public class DistRemoveValidator : AbstractValidator<DistRemove>
{
    public DistRemoveValidator()
    {
        RuleFor(x => x.Version)
            .NotEmpty()
            .WithMessage("Version is required");

        RuleFor(x => x.Platform)
            .Must((cmd, platform) => platform == null || Infrastructure.RuntimeIdentifier.IsValidRid(platform))
            .WithMessage("Platform must be a valid Runtime Identifier (e.g., linux-x64, win-x64, osx-arm64)");
    }
}
