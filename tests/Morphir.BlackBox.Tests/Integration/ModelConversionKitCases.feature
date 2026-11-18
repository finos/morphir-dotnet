@integration @needs-model-conversion
Feature: Model conversion kit cases
  As a developer who uses Morphir
  I want model convert to meet the kit's document-tree cases
  So that the trees it reads and writes are the trees of the parent's specification

  One scenario for each pinned case of the kit's document-tree.feature (FixtureCatalog.Kit), named with its kit id
  and title. The expectations come from the catalog: the canonical single file, the tree files and the diagnostic
  of each case. A case with a tree converts it to a single file, and a case whose files a writer writes back
  converts that single file to a tree again. A case that pins one file's spelling converts a tree that holds the
  file there and back. The tree that model convert writes has the path budget 4000, so the cases with another budget
  (document-tree-0004 and document-tree-0014) are checked on the read only; the store's own tests write them with
  their budget. KitCaseCoverageTests in tests/Morphir.Build.Tests pins this list of scenarios to the catalog.

  Scenario: document-tree-0001 Manifest file
    Given a tree with the file of the kit case "document-tree-0001"
    When I convert the model to a single file
    And I convert the written model to a document tree
    Then the conversion succeeds
    And the written tree has the file of the kit case in its canonical spelling

  Scenario: document-tree-0002 Module manifest, names only
    Given a tree with the file of the kit case "document-tree-0002"
    When I convert the model to a single file
    And I convert the written model to a document tree
    Then the conversion succeeds
    And the written tree has the file of the kit case in its canonical spelling

  Scenario: document-tree-0002 Module manifest, names only, in its accepted spelling
    Given a tree with the accepted file spelling of the kit case "document-tree-0002"
    When I convert the model to a single file
    And I convert the written model to a document tree
    Then the conversion succeeds
    And the written tree has the file of the kit case in its canonical spelling

  Scenario: document-tree-0003 Node filename is the escaped stem
    Given the tree of the kit case "document-tree-0003"
    When I convert the model to a single file
    Then the conversion succeeds
    And the written single file is the canonical spelling of the kit case
    When I convert the written model to a document tree
    Then the conversion succeeds
    And the written tree has the files of the kit case

  Scenario: document-tree-0004 A stem truncated for the path budget is recorded in fileNames
    Given the tree of the kit case "document-tree-0004"
    When I convert the model to a single file
    Then the conversion succeeds
    And the written single file is the canonical spelling of the kit case

  Scenario: document-tree-0005 A top-level $meta member is reserved and ignored
    Given the tree of the kit case "document-tree-0005"
    When I convert the model to a single file
    Then the conversion succeeds
    And the written single file is the canonical spelling of the kit case

  Scenario: document-tree-0006 The same tree in the JSON profile
    Given the tree of the kit case "document-tree-0006"
    When I convert the model to a single file
    Then the conversion succeeds
    And the written single file is the canonical spelling of the kit case
    When I convert the written model to a document tree
    Then the conversion succeeds
    And the written tree has the files of the kit case

  Scenario: document-tree-0007 A Private module
    Given the tree of the kit case "document-tree-0007"
    When I convert the model to a single file
    Then the conversion succeeds
    And the written single file is the canonical spelling of the kit case
    When I convert the written model to a document tree
    Then the conversion succeeds
    And the written tree has the files of the kit case

  Scenario: document-tree-0008 A dependency lives under deps
    Given the tree of the kit case "document-tree-0008"
    When I convert the model to a single file
    Then the conversion succeeds
    And the written single file is the canonical spelling of the kit case
    When I convert the written model to a document tree
    Then the conversion succeeds
    And the written tree has the files of the kit case

  Scenario: document-tree-0009 An application's dependencies are definitions under deps
    Given the tree of the kit case "document-tree-0009"
    When I convert the model to a single file
    Then the conversion succeeds
    And the written single file is the canonical spelling of the kit case
    When I convert the written model to a document tree
    Then the conversion succeeds
    And the written tree has the files of the kit case

  Scenario: document-tree-0010 v3 manifest
    Given a tree with the file of the kit case "document-tree-0010"
    When I convert the model to a single file
    And I convert the written model to a document tree
    Then the conversion succeeds
    And the written tree has the file of the kit case in its canonical spelling

  Scenario: document-tree-0011 v3 Library module with definition files
    Given the tree of the kit case "document-tree-0011"
    When I convert the model to a single file
    Then the conversion succeeds
    And the written single file is the canonical spelling of the kit case
    When I convert the written model to a document tree
    Then the conversion succeeds
    And the written tree has the files of the kit case

  Scenario: document-tree-0012 v3 dependency specifications under deps
    Given the tree of the kit case "document-tree-0012"
    When I convert the model to a single file
    Then the conversion succeeds
    And the written single file is the canonical spelling of the kit case
    When I convert the written model to a document tree
    Then the conversion succeeds
    And the written tree has the files of the kit case

  Scenario: document-tree-0013 v3 Specs tree
    Given the tree of the kit case "document-tree-0013"
    When I convert the model to a single file
    Then the conversion succeeds
    And the written single file is the canonical spelling of the kit case
    When I convert the written model to a document tree
    Then the conversion succeeds
    And the written tree has the files of the kit case

  Scenario: document-tree-0014 v3 cut stem in fileNames
    Given the tree of the kit case "document-tree-0014"
    When I convert the model to a single file
    Then the conversion succeeds
    And the written single file is the canonical spelling of the kit case

  Scenario: document-tree-0015 v3 JSON profile
    Given the tree of the kit case "document-tree-0015"
    When I convert the model to a single file
    Then the conversion succeeds
    And the written single file is the canonical spelling of the kit case
    When I convert the written model to a document tree
    Then the conversion succeeds
    And the written tree has the files of the kit case

  Scenario: document-tree-0016 A v4 document offered as a v3 Distribution is rejected
    Given the tree of the kit case "document-tree-0015" with the rejected document of the kit case "document-tree-0016" as its type file
    When I convert the model to a single file
    Then the conversion fails with the diagnostic of the kit case at the rejected file
    And nothing is at the output

  Scenario: document-tree-0017 v3 tree files ignore $meta
    Given the tree of the kit case "document-tree-0017"
    When I convert the model to a single file
    Then the conversion succeeds
    And the written single file is the canonical spelling of the kit case
