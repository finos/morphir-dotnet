@e2e @needs-development-packages
Feature: The migrated F# SDK packages keep the existing consumers working
  The canonical package Morphir.SDK.FSharp owns the runtime assembly Morphir.SDK.dll. The compatibility package
  Morphir.SDK has no assembly and only a dependency on the canonical package. A consumer that installs either
  package or both (a package graph) compiles with unchanged source, and a binary that was compiled against the old
  package runs with no new compilation. The test builds and packs both packages in its workspace.

  Scenario: Source and precompiled consumers of each package graph run on the canonical runtime
    Given the SDK packages built and packed in the workspace with the version of the changelog
    Then the package has no assembly
    And the package depends only on "Morphir.SDK.FSharp" at exactly its own version
    And the build of the package "Morphir.SDK" in the workspace made no assembly
    And the precompiled legacy consumer has its recorded SHA-256
    When a consumer of the package graph "canonical" restores, builds and runs
    Then the consumer resolves one runtime assembly, from the package "Morphir.SDK.FSharp"
    And the consumer resolves only the packages of its graph
    And the runtime assembly in the output of the consumer is the one of the package "Morphir.SDK.FSharp", with its identity
    And the consumer keeps each public declaration of the legacy runtime
    And the precompiled legacy consumer ran unchanged on the runtime of the package "Morphir.SDK.FSharp"
    When a consumer of the package graph "legacy" restores, builds and runs
    Then the consumer resolves one runtime assembly, from the package "Morphir.SDK.FSharp"
    And the consumer resolves only the packages of its graph
    And the runtime assembly in the output of the consumer is the one of the package "Morphir.SDK.FSharp", with its identity
    And the consumer keeps each public declaration of the legacy runtime
    And the precompiled legacy consumer ran unchanged on the runtime of the package "Morphir.SDK.FSharp"
    When a consumer of the package graph "combined" restores, builds and runs
    Then the consumer resolves one runtime assembly, from the package "Morphir.SDK.FSharp"
    And the consumer resolves only the packages of its graph
    And the runtime assembly in the output of the consumer is the one of the package "Morphir.SDK.FSharp", with its identity
    And the consumer keeps each public declaration of the legacy runtime
    And the precompiled legacy consumer ran unchanged on the runtime of the package "Morphir.SDK.FSharp"
    When a consumer of the generated Ordering source of "classic-3" builds and runs against the package "Morphir.SDK.FSharp"
    Then the consumer prints "Generated Order: 4 assertions passed"
    And the runtime assembly in the output of the consumer is the runtime of the package "Morphir.SDK.FSharp"
    When a consumer of the generated Ordering source of "v4-4.0.0" builds and runs against the package "Morphir.SDK.FSharp"
    Then the consumer prints "Generated Order: 4 assertions passed"
    And the runtime assembly in the output of the consumer is the runtime of the package "Morphir.SDK.FSharp"

  Scenario: A later package version keeps the source and the old binary compatibility
    Given the SDK packages built and packed in the workspace at the version "0.4.1-sdkproof"
    Then the package has no assembly
    And the package depends only on "Morphir.SDK.FSharp" at exactly its own version
    And the build of the package "Morphir.SDK" in the workspace made no assembly
    And the precompiled legacy consumer has its recorded SHA-256
    When a consumer of the package graph "canonical" restores, builds and runs
    Then the consumer resolves one runtime assembly, from the package "Morphir.SDK.FSharp"
    And the consumer resolves only the packages of its graph
    And the runtime assembly in the output of the consumer is the one of the package "Morphir.SDK.FSharp", with its identity
    And the consumer keeps each public declaration of the legacy runtime
    And the precompiled legacy consumer ran unchanged on the runtime of the package "Morphir.SDK.FSharp"
    When a consumer of the package graph "legacy" restores, builds and runs
    Then the consumer resolves one runtime assembly, from the package "Morphir.SDK.FSharp"
    And the consumer resolves only the packages of its graph
    And the runtime assembly in the output of the consumer is the one of the package "Morphir.SDK.FSharp", with its identity
    And the consumer keeps each public declaration of the legacy runtime
    And the precompiled legacy consumer ran unchanged on the runtime of the package "Morphir.SDK.FSharp"
    When a consumer of the package graph "combined" restores, builds and runs
    Then the consumer resolves one runtime assembly, from the package "Morphir.SDK.FSharp"
    And the consumer resolves only the packages of its graph
    And the runtime assembly in the output of the consumer is the one of the package "Morphir.SDK.FSharp", with its identity
    And the consumer keeps each public declaration of the legacy runtime
    And the precompiled legacy consumer ran unchanged on the runtime of the package "Morphir.SDK.FSharp"
    When a consumer of the generated Ordering source of "classic-3" builds and runs against the package "Morphir.SDK.FSharp"
    Then the consumer prints "Generated Order: 4 assertions passed"
    And the runtime assembly in the output of the consumer is the runtime of the package "Morphir.SDK.FSharp"
    When a consumer of the generated Ordering source of "v4-4.0.0" builds and runs against the package "Morphir.SDK.FSharp"
    Then the consumer prints "Generated Order: 4 assertions passed"
    And the runtime assembly in the output of the consumer is the runtime of the package "Morphir.SDK.FSharp"
