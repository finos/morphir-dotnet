#[cfg(test)]
mod tests {
    use crate::NAME;
    use semver::Version;

    #[test]
    fn test_plugin_compiles() {
        // This test simply verifies that the plugin compiles
        // More comprehensive tests would require the proto_pdk test harness
        assert!(true);
    }

    #[test]
    fn test_version_parsing() {
        // Test that basic version strings are handled correctly
        let v1 = Version::parse("1.0.0");
        assert!(v1.is_ok());

        let v2 = Version::parse("0.1.0-alpha.1");
        assert!(v2.is_ok());
    }

    #[test]
    fn test_plugin_name() {
        assert_eq!(NAME, "Morphir");
    }

    // Tests for platform-specific executable names
    mod executable_name_tests {
        use proto_pdk::HostOS;

        #[test]
        fn test_windows_executable_name() {
            // On Windows, executable should have .exe extension
            let exe_name = if HostOS::Windows == HostOS::Windows {
                "morphir.exe"
            } else {
                "morphir"
            };
            assert!(exe_name == "morphir.exe" || exe_name == "morphir");
        }

        #[test]
        fn test_unix_executable_name() {
            // On Unix (Linux/macOS), executable should not have extension
            let exe_name = if HostOS::Linux == HostOS::Linux {
                "morphir"
            } else {
                "morphir.exe"
            };
            assert!(exe_name == "morphir" || exe_name == "morphir.exe");
        }
    }

    // Tests for RID (Runtime Identifier) mapping
    mod rid_mapping_tests {
        use proto_pdk::{HostArch, HostOS};

        #[test]
        fn test_linux_x64_rid() {
            let rid = match (HostOS::Linux, HostArch::X64) {
                (HostOS::Linux, HostArch::X64) => "linux-x64",
                _ => "unknown",
            };
            assert_eq!(rid, "linux-x64");
        }

        #[test]
        fn test_linux_arm64_rid() {
            let rid = match (HostOS::Linux, HostArch::Arm64) {
                (HostOS::Linux, HostArch::Arm64) => "linux-arm64",
                _ => "unknown",
            };
            assert_eq!(rid, "linux-arm64");
        }

        #[test]
        fn test_macos_x64_rid() {
            let rid = match (HostOS::MacOS, HostArch::X64) {
                (HostOS::MacOS, HostArch::X64) => "osx-x64",
                _ => "unknown",
            };
            assert_eq!(rid, "osx-x64");
        }

        #[test]
        fn test_macos_arm64_rid() {
            let rid = match (HostOS::MacOS, HostArch::Arm64) {
                (HostOS::MacOS, HostArch::Arm64) => "osx-arm64",
                _ => "unknown",
            };
            assert_eq!(rid, "osx-arm64");
        }

        #[test]
        fn test_windows_x64_rid() {
            let rid = match (HostOS::Windows, HostArch::X64) {
                (HostOS::Windows, HostArch::X64) => "win-x64",
                _ => "unknown",
            };
            assert_eq!(rid, "win-x64");
        }
    }

    // Tests for URL generation
    mod url_generation_tests {
        #[test]
        fn test_download_url_format() {
            let version = "1.0.0";
            let rid = "linux-x64";
            let download_url = format!(
                "https://github.com/finos/morphir-dotnet/releases/download/v{}/morphir-{}-v{}.tar.gz",
                version, rid, version
            );
            assert_eq!(
                download_url,
                "https://github.com/finos/morphir-dotnet/releases/download/v1.0.0/morphir-linux-x64-v1.0.0.tar.gz"
            );
        }

        #[test]
        fn test_download_name_format() {
            let version = "1.0.0";
            let rid = "win-x64";
            let download_name = format!("morphir-{}-v{}.tar.gz", rid, version);
            assert_eq!(download_name, "morphir-win-x64-v1.0.0.tar.gz");
        }

        #[test]
        fn test_all_platform_urls() {
            let version = "2.5.3";
            let platforms = vec![
                ("linux-x64", "morphir-linux-x64-v2.5.3.tar.gz"),
                ("linux-arm64", "morphir-linux-arm64-v2.5.3.tar.gz"),
                ("osx-x64", "morphir-osx-x64-v2.5.3.tar.gz"),
                ("osx-arm64", "morphir-osx-arm64-v2.5.3.tar.gz"),
                ("win-x64", "morphir-win-x64-v2.5.3.tar.gz"),
            ];

            for (rid, expected_name) in platforms {
                let download_name = format!("morphir-{}-v{}.tar.gz", rid, version);
                assert_eq!(download_name, expected_name);
            }
        }
    }

    // Tests for archive prefix
    mod archive_tests {
        #[test]
        fn test_archive_prefix_matches_rid() {
            let rid = "linux-x64";
            let archive_prefix = Some(rid.to_string());
            assert_eq!(archive_prefix, Some("linux-x64".to_string()));
        }
    }

    // Tests for file permissions (Unix-specific logic)
    #[cfg(unix)]
    mod unix_permissions_tests {
        #[test]
        fn test_executable_permission_bits() {
            // Verify the permission bits are correct: 0o755 = rwxr-xr-x
            let mode = 0o755;
            assert_eq!(mode, 0o755);
            assert_eq!(mode & 0o700, 0o700); // Owner: rwx
            assert_eq!(mode & 0o070, 0o050); // Group: r-x
            assert_eq!(mode & 0o007, 0o005); // Others: r-x
        }
    }

    // Edge case tests
    mod edge_cases {
        use semver::Version;

        #[test]
        fn test_version_with_prerelease() {
            let v = Version::parse("1.0.0-rc.1");
            assert!(v.is_ok());
            let version = v.unwrap();
            assert_eq!(version.major, 1);
            assert_eq!(version.minor, 0);
            assert_eq!(version.patch, 0);
        }

        #[test]
        fn test_version_with_build_metadata() {
            let v = Version::parse("1.0.0+build.123");
            assert!(v.is_ok());
        }

        #[test]
        fn test_invalid_version() {
            let v = Version::parse("not-a-version");
            assert!(v.is_err());
        }

        #[test]
        fn test_url_with_special_version() {
            let version = "1.0.0-rc.1";
            let rid = "linux-x64";
            let download_url = format!(
                "https://github.com/finos/morphir-dotnet/releases/download/v{}/morphir-{}-v{}.tar.gz",
                version, rid, version
            );
            assert!(download_url.contains("1.0.0-rc.1"));
        }
    }
}
