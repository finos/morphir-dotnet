use extism_pdk::*;
use proto_pdk::*;
use std::collections::HashMap;

#[cfg(test)]
mod tests;

static NAME: &str = "Morphir";

/// Register the Morphir tool with proto
#[plugin_fn]
pub fn register_tool(Json(_): Json<RegisterToolInput>) -> FnResult<Json<RegisterToolOutput>> {
    Ok(Json(RegisterToolOutput {
        name: NAME.into(),
        type_of: PluginType::CommandLine,
        plugin_version: Version::parse(env!("CARGO_PKG_VERSION")).ok(),
        minimum_proto_version: Some(Version::new(0, 32, 0)),
        ..RegisterToolOutput::default()
    }))
}

/// Detect the Morphir version from a directory
#[plugin_fn]
pub fn detect_version_files(_: ()) -> FnResult<Json<DetectVersionOutput>> {
    // Morphir doesn't use version files in projects
    Ok(Json(DetectVersionOutput {
        files: vec![],
        ignore: vec![],
    }))
}

/// Load available Morphir versions
#[plugin_fn]
pub fn load_versions(Json(_): Json<LoadVersionsInput>) -> FnResult<Json<LoadVersionsOutput>> {
    let output = LoadVersionsOutput::default();
    
    // Fetch versions from GitHub Releases API
    // For now, return empty list - versions should be managed by GitHub Releases
    // Users can specify explicit versions which will be resolved
    
    Ok(Json(output))
}

/// Resolve a version specification to a concrete version
#[plugin_fn]
pub fn resolve_version(
    Json(_input): Json<ResolveVersionInput>,
) -> FnResult<Json<ResolveVersionOutput>> {
    let output = ResolveVersionOutput::default();
    
    // For now, just use the input version as-is
    // In a more sophisticated implementation, we would:
    // 1. Fetch latest from GitHub API for "latest" alias
    // 2. Support pre-release versions
    // 3. Validate version exists
    
    Ok(Json(output))
}

/// Download the Morphir executable for a specific version
#[plugin_fn]
pub fn download_prebuilt(
    Json(input): Json<DownloadPrebuiltInput>,
) -> FnResult<Json<DownloadPrebuiltOutput>> {
    let env = get_host_environment()?;
    
    // Determine the platform-specific executable name and RID
    let rid = match (env.os, env.arch) {
        (HostOS::Linux, HostArch::X64) => "linux-x64",
        (HostOS::Linux, HostArch::Arm64) => "linux-arm64",
        (HostOS::MacOS, HostArch::X64) => "osx-x64",
        (HostOS::MacOS, HostArch::Arm64) => "osx-arm64",
        (HostOS::Windows, HostArch::X64) => "win-x64",
        _ => {
            return Err(plugin_err!(
                "Unsupported platform: {} {}",
                env.os,
                env.arch
            ));
        }
    };
    
    let version = &input.context.version;
    
    // GitHub Release URL format
    let download_url = format!(
        "https://github.com/finos/morphir-dotnet/releases/download/v{}/morphir-{}-v{}.tar.gz",
        version, rid, version
    );
    
    let download_name = format!("morphir-{}-v{}.tar.gz", rid, version);
    
    Ok(Json(DownloadPrebuiltOutput {
        archive_prefix: Some(rid.to_string()),
        download_url,
        download_name: Some(download_name),
        ..DownloadPrebuiltOutput::default()
    }))
}

/// Locate the Morphir executable within the installed directory
#[plugin_fn]
pub fn locate_executables(
    Json(_input): Json<LocateExecutablesInput>,
) -> FnResult<Json<LocateExecutablesOutput>> {
    let env = get_host_environment()?;
    
    // Determine the executable name based on OS
    let exe_name = if env.os == HostOS::Windows {
        "morphir.exe"
    } else {
        "morphir"
    };
    
    Ok(Json(LocateExecutablesOutput {
        exes: HashMap::from_iter([(
            "morphir".into(),
            ExecutableConfig::new_primary(exe_name),
        )]),
        ..LocateExecutablesOutput::default()
    }))
}

/// Called after installation to perform any post-install setup
#[plugin_fn]
pub fn post_install(Json(input): Json<InstallHook>) -> FnResult<()> {
    let env = get_host_environment()?;
    let exe_path = input.context.tool_dir.join(if env.os == HostOS::Windows {
        "morphir.exe"
    } else {
        "morphir"
    });
    
    // Set executable permissions on Unix systems
    #[cfg(unix)]
    {
        use std::fs;
        use std::os::unix::fs::PermissionsExt;
        
        if let Ok(metadata) = fs::metadata(&exe_path) {
            let mut permissions = metadata.permissions();
            permissions.set_mode(0o755);
            let _ = fs::set_permissions(&exe_path, permissions);
        }
    }
    
    // Avoid unused variable warning on non-Unix platforms
    #[cfg(not(unix))]
    let _ = exe_path;
    
    Ok(())
}

/// Called before uninstallation
#[plugin_fn]
pub fn pre_uninstall(Json(_): Json<InstallHook>) -> FnResult<()> {
    // No special cleanup needed
    Ok(())
}
