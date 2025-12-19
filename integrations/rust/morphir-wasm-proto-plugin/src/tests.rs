#[cfg(test)]
mod tests {
    #[test]
    fn test_plugin_compiles() {
        // This test simply verifies that the plugin compiles
        // More comprehensive tests would require the proto_pdk test harness
        assert!(true);
    }

    #[test]
    fn test_version_parsing() {
        // Test that basic version strings are handled correctly
        use semver::Version;
        
        let v1 = Version::parse("1.0.0");
        assert!(v1.is_ok());
        
        let v2 = Version::parse("0.1.0-alpha.1");
        assert!(v2.is_ok());
    }
}
