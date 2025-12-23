using FluentValidation;

namespace Morphir.Tooling.Features.Management;

// ===== Tool List Command =====

public record ToolList(
    string? Platform = null,
    bool Local = false
);

public record ToolListResult(
    List<ToolInfo> Tools,
    string Platform,
    bool IsLocal
);

public record ToolInfo(
    string Name,
    List<string> InstalledVersions,
    string? ActiveVersion,
    string Platform
);

// ===== Tool Install Command =====

public record ToolInstall(
    string Name,
    string SourceUrl,
    string Version,
    string? Platform = null,
    bool Local = false
);

public record ToolInstallResult(
    bool Success,
    string Name,
    string Version,
    string Platform,
    string InstalledPath,
    string? ErrorMessage = null
);

// ===== Tool Use Command =====

public record ToolUse(
    string Name,
    string Version,
    string? Platform = null,
    bool Local = false
);

public record ToolUseResult(
    bool Success,
    string Name,
    string Version,
    string Platform,
    bool IsLocal,
    string? ErrorMessage = null
);

// ===== Tool Remove Command =====

public record ToolRemove(
    string Name,
    string Version,
    string? Platform = null,
    bool Local = false
);

public record ToolRemoveResult(
    bool Success,
    string Name,
    string Version,
    string Platform,
    bool IsLocal,
    string? ErrorMessage = null
);

// ===== Tool Which Command =====

public record ToolWhich(
    string Name,
    string? Platform = null
);

public record ToolWhichResult(
    bool Found,
    string Name,
    string? Version,
    string? Platform,
    string? Path,
    bool IsLocal,
    string? ErrorMessage = null
);

// ===== Validators =====

public class ToolInstallValidator : AbstractValidator<ToolInstall>
{
    public ToolInstallValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Tool name is required");

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

public class ToolUseValidator : AbstractValidator<ToolUse>
{
    public ToolUseValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Tool name is required");

        RuleFor(x => x.Version)
            .NotEmpty()
            .WithMessage("Version is required");

        RuleFor(x => x.Platform)
            .Must((cmd, platform) => platform == null || Infrastructure.RuntimeIdentifier.IsValidRid(platform))
            .WithMessage("Platform must be a valid Runtime Identifier (e.g., linux-x64, win-x64, osx-arm64)");
    }
}

public class ToolRemoveValidator : AbstractValidator<ToolRemove>
{
    public ToolRemoveValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Tool name is required");

        RuleFor(x => x.Version)
            .NotEmpty()
            .WithMessage("Version is required");

        RuleFor(x => x.Platform)
            .Must((cmd, platform) => platform == null || Infrastructure.RuntimeIdentifier.IsValidRid(platform))
            .WithMessage("Platform must be a valid Runtime Identifier (e.g., linux-x64, win-x64, osx-arm64)");
    }
}

public class ToolWhichValidator : AbstractValidator<ToolWhich>
{
    public ToolWhichValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Tool name is required");

        RuleFor(x => x.Platform)
            .Must((cmd, platform) => platform == null || Infrastructure.RuntimeIdentifier.IsValidRid(platform))
            .WithMessage("Platform must be a valid Runtime Identifier (e.g., linux-x64, win-x64, osx-arm64)");
    }
}
