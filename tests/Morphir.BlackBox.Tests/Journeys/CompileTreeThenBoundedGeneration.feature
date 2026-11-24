@journey @host-tool @needs-compilation @needs-generation @needs-in-process-fsharp-frontend @needs-built-extensions
Feature: Generate with bounded model access from the document tree of a real compile
  The Orders domain source compiles with ir.layout = "document-tree". morphir generate without --input then reads
  the recorded tree through a bounded session with the orders backend extension, as the compile page documents. Both
  commands use the default out root of the project.

  Scenario: A document-tree compile of the Orders source feeds bounded generation
    Given the Orders domain source in a document-tree project
    When I compile the project
    Then the compile records the Orders document tree
    And bounded generation renders order 73 from the compile record
