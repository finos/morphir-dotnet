Feature: Document JSON Encoding

This feature tests the serialization and deserialization of Document types to/from JSON.
Documents map naturally to JSON with extended types using tagged format for Char, Uri, Uuid, and Bytes.

  Rule: Primitive types should serialize to native JSON values

    Scenario: Null Document should serialize to JSON null
      Given a Document of type Null
      When we serialize it to JSON
      Then the JSON should be:
      """
      null
      """

    Scenario: Boolean true Document should round-trip
      Given a Document encoded as:
      """
      true
      """
      When we deserialize it
      And we serialize it back
      Then the result should match the original input

    Scenario: Boolean false Document should round-trip
      Given a Document encoded as:
      """
      false
      """
      When we deserialize it
      And we serialize it back
      Then the result should match the original input

    Scenario: String Documents should preserve content
      Given a Document encoded as:
      """
      "Hello, World!"
      """
      When we deserialize it
      And we serialize it back
      Then the result should match the original input

    Scenario: String with special characters should round-trip
      Given a Document encoded as:
      """
      "Line 1\nLine 2\tTabbed"
      """
      When we deserialize it
      And we serialize it back
      Then the result should match the original input

    Scenario: Integer Documents should serialize as JSON numbers
      Given a Document encoded as:
      """
      42
      """
      When we deserialize it
      And we serialize it back
      Then the result should match the original input

    Scenario: Negative integers should round-trip
      Given a Document encoded as:
      """
      -100
      """
      When we deserialize it
      And we serialize it back
      Then the result should match the original input

    Scenario: Decimal Numbers should round-trip
      Given a Document encoded as:
      """
      3.14
      """
      When we deserialize it
      And we serialize it back
      Then the result should match the original input

  Rule: Extended types should use tagged format

    Scenario: Uri Documents should use $uri tag
      Given a Document encoded as:
      """
      {"$uri":"https://example.com/"}
      """
      When we deserialize it
      And we serialize it back
      Then the result should match the original input

    Scenario: Uuid Documents should use $uuid tag
      Given a Document encoded as:
      """
      {"$uuid":"550e8400-e29b-41d4-a716-446655440000"}
      """
      When we deserialize it
      And we serialize it back
      Then the result should match the original input

    Scenario: Bytes Documents should use $bytes tag with base64
      Given a Document encoded as:
      """
      {"$bytes":"SGVsbG8="}
      """
      When we deserialize it
      And we serialize it back
      Then the result should match the original input

  Rule: Collections should serialize recursively

    Scenario: Empty Array Documents should round-trip
      Given a Document encoded as:
      """
      []
      """
      When we deserialize it
      And we serialize it back
      Then the result should match the original input

    Scenario: Array Documents should preserve order
      Given a Document encoded as:
      """
      [null,true,42,"hello"]
      """
      When we deserialize it
      And we serialize it back
      Then the result should be equivalent to the original input

    Scenario: Nested arrays should round-trip
      Given a Document encoded as:
      """
      [[1,2],[3,4]]
      """
      When we deserialize it
      And we serialize it back
      Then the result should be equivalent to the original input

    Scenario: Empty Object Documents should round-trip
      Given a Document encoded as:
      """
      {}
      """
      When we deserialize it
      And we serialize it back
      Then the result should match the original input

    Scenario: Object Documents should preserve key-value pairs
      Given a Document encoded as:
      """
      {"name":"Alice","age":30,"active":true}
      """
      When we deserialize it
      And we serialize it back
      Then the result should be equivalent to the original input

    Scenario: Nested objects should round-trip
      Given a Document encoded as:
      """
      {"user":{"name":"Bob","id":123}}
      """
      When we deserialize it
      And we serialize it back
      Then the result should be equivalent to the original input

  Rule: Complex nested structures should round-trip correctly

    Scenario: Deeply nested Document with mixed types
      Given a Document encoded as:
      """
      {
        "user": {
          "name": "Alice",
          "age": 30,
          "contact": {
            "$uri": "https://example.com/alice/"
          }
        },
        "tags": ["important", "verified"],
        "metadata": {
          "$uuid": "550e8400-e29b-41d4-a716-446655440000"
        }
      }
      """
      When we deserialize it
      And we serialize it back
      Then the result should be equivalent to the original input

    Scenario: Array with all primitive types
      Given a Document encoded as:
      """
      [null,true,false,"text",123,3.14]
      """
      When we deserialize it
      And we serialize it back
      Then the result should be equivalent to the original input

    Scenario: Object with extended types
      Given a Document encoded as:
      """
      {"uri":{"$uri":"https://test.com/"},"uuid":{"$uuid":"6ba7b810-9dad-11d1-80b4-00c04fd430c8"},"bytes":{"$bytes":"AQIDBA=="}}
      """
      When we deserialize it
      And we serialize it back
      Then the result should be equivalent to the original input

    Scenario Outline: Various Document types round-trip via outline
      Given a Document encoded as JSON text: <DocumentJson>
      When we deserialize it
      And we serialize it back
      Then the result should be equivalent to the original input

      Examples:
      | DocumentJson |
      | '[1,2,3,4,5]' |
      | '{"numbers":[1,2,3],"text":"hello"}' |
      | '{"nested":{"deep":{"value":true}}}' |
      | '[{"id":1},{"id":2}]' |
