# Spec Delta

## Purpose

Gives every morphir-dotnet host, guest and frontend one implementation of the Morphir Extension Protocol (MEP) `0.2.0-draft.1`: JSON-RPC 2.0 messages, framing, the session lifecycle and the model transport, as the pinned upstream contract defines them.

## ADDED Requirements

### Requirement: Messages follow the pinned MEP contract
Every MEP message SHALL use the methods, member names and value types of the pinned upstream contract `0.2.0-draft.1` (`tests/Fixtures/Mep/`). morphir-dotnet SHALL NOT add a method, member or error code to the contract. An absent optional member SHALL be left out of the JSON, and never written as `null`, unless the contract says otherwise.

#### Scenario: Pinned transcripts round-trip
- **WHEN** each lifecycle, session and protocol-error message in the pinned transcripts is read and written again
- **THEN** the written JSON has the same members, values and value types as the transcript

#### Scenario: Types agree with the contract model
- **WHEN** the implemented MEP types are compared with the pinned `mep.ir.json`
- **THEN** each implemented type has the contract's member names, and each required contract member is present

### Requirement: Request ids keep their exact value
A request id SHALL be an unsigned 64-bit integer or a string. A response SHALL carry the id of its request as the same JSON token. A numeric id outside the unsigned 64-bit range, a negative id or a fraction SHALL get the error reply `-32600`, and the connection SHALL stay open.

#### Scenario: Large numeric ids
- **WHEN** a peer sends requests with the ids `42`, `9223372036854775808` and `18446744073709551615`
- **THEN** each response carries the same numeric token as its request

#### Scenario: String ids
- **WHEN** a peer sends requests with the ids `"req-abc"` and `""`
- **THEN** each response carries the same string id

#### Scenario: Bad id
- **WHEN** a peer sends a request with the id `-1` or `1.5`
- **THEN** the reply is an error with the code `-32600`, and a later valid request gets its normal response

### Requirement: Bad messages get an error reply
A message that is not valid JSON SHALL get the error reply `-32700` with the id `null`. A message that is JSON but not a JSON-RPC 2.0 request, notification or response SHALL get `-32600`. A request for a method that the receiver does not serve SHALL get `-32601`. Params that do not match the method's type SHALL get `-32602`. In each case the session SHALL stay usable.

#### Scenario: Unknown method
- **WHEN** an initialized peer sends a request with an unknown method
- **THEN** the reply is `-32601`, and the next valid request succeeds

#### Scenario: Params object with one member
- **WHEN** a peer sends `morphir.extension.describe` with params that hold only `protocolVersions`
- **THEN** the receiver reads the params as the complete describe-params object and replies with the extension description

### Requirement: Notifications get no reply
A message with no `id` SHALL be handled as a notification. The receiver SHALL NOT send a response to a notification, also when the notification fails.

#### Scenario: Initialized notification
- **WHEN** a host sends `morphir.initialized` after a successful `initialize`
- **THEN** the guest sends no message in reply

### Requirement: Stream framing uses Content-Length headers
On a byte stream, each message SHALL be one frame: a `Content-Length` header in ASCII, an empty line, and then the UTF-8 JSON body of exactly that length. A reader SHALL refuse a header block over 8192 bytes, a body over 64 MiB, a missing or malformed length, and a stream that ends inside a frame. A refused frame SHALL fail the connection with a protocol diagnostic. It SHALL NOT be skipped.

#### Scenario: Frames split across reads
- **WHEN** a frame arrives in many small reads, with a body that holds non-ASCII text
- **THEN** the reader returns the one complete message with the correct text

#### Scenario: Body over the limit
- **WHEN** a frame declares a body of 64 MiB plus one byte
- **THEN** the reader refuses the frame before it reads the body, and reports the limit

#### Scenario: Text outside a frame
- **WHEN** a peer writes a line of log text to the stream instead of a frame
- **THEN** the connection fails with a protocol diagnostic that says the stream is not framed

### Requirement: One dispatch path serves streams and single messages
A guest SHALL handle a message in the same way whether it arrives in a frame on a stream or as one message passed alone, as the WASM runtime passes it. Single-message dispatch SHALL return the response bytes for a request and no bytes for a notification.

#### Scenario: Same reply on both paths
- **WHEN** the same `morphir.extension.describe` request is sent to a guest through a framed stream and through single-message dispatch
- **THEN** the two response bodies are equal byte for byte

### Requirement: Requests can be cancelled
A `$/cancelRequest` notification SHALL cancel the pending request with the given id, of either id kind. A cancelled request SHALL get the error reply `-32800`. A cancel for an id that is not pending SHALL be ignored.

#### Scenario: Cancel a running request
- **WHEN** a host sends a slow request with the id `18446744073709551615` and then `$/cancelRequest` for that id
- **THEN** the handler sees the cancellation, and the reply to that id is `-32800`

### Requirement: Sessions follow the MEP lifecycle
A session SHALL accept only `morphir.extension.describe` and `morphir.initialize` before initialization. Any other request before initialization or after `morphir.shutdown` SHALL get `-32014`. A second `initialize` SHALL fail. `morphir.exit` SHALL end the session. Hosts and guests SHALL apply the same lifecycle rules.

#### Scenario: Request before initialization
- **WHEN** a host sends `morphir.extension.capabilities` before `morphir.initialize`
- **THEN** the reply is `-32014`, as in the pinned lifecycle transcript

#### Scenario: Request after shutdown
- **WHEN** a host sends `morphir.extension.info` after a successful `morphir.shutdown`
- **THEN** the reply is `-32014`

### Requirement: Only protocol version 0.2.0-draft.1 is spoken
A host SHALL offer only `0.2.0-draft.1`, and a guest SHALL claim and accept only that version, by exact match. When the offered versions and the claimed versions have no common member, `initialize` SHALL fail with `-32011` and the session SHALL stay uninitialized. The legacy protocol `0.1` SHALL NOT be offered or accepted.

#### Scenario: Legacy-only host
- **WHEN** a host initializes with `protocolVersions: ["0.1"]`
- **THEN** the guest replies `-32011`, and a later `initialize` with `0.2.0-draft.1` succeeds

### Requirement: A request needs an advertised capability
After initialization, a request for a capability that the guest did not advertise SHALL get `-32013`. A host SHALL check the initialized capabilities and SHALL NOT send such a request.

#### Scenario: Capability not advertised
- **WHEN** a guest advertises no backend capability and a peer sends `morphir.backend.generateModel`
- **THEN** the reply is `-32013`, as in the pinned lifecycle transcript

### Requirement: Model transport tuples are negotiated exactly
A host SHALL offer complete model-transport tuples (operation, profile, codec, envelope version, IR version) in its order of preference, with its limits. A guest SHALL select the offered tuples it supports, in host order, with each limit at the smaller of the two values. A host SHALL refuse a selection that holds a tuple it did not offer or a limit above its own. An empty agreement for an operation SHALL make that operation unavailable.

#### Scenario: Host order kept
- **WHEN** a host offers tuples at IR versions `4.0.0` and then `3.0.0`, and the guest supports both
- **THEN** the guest selects both in that order

#### Scenario: Guest selects a tuple that was not offered
- **WHEN** a guest selects a tuple with a codec that the host did not offer
- **THEN** the host refuses the session with a diagnostic that names the extension and the tuple

### Requirement: Model envelopes are verified before decoding
A `MorphirModel` envelope SHALL be admitted only after these checks, in this order: the outer size limit, base64 bounds and canonical padded form, the decoded length, the lowercase SHA-256 of the decoded bytes, JSON depth and node limits, and the profile, envelope version and IR version against the negotiated tuple. A failed check SHALL give `-32602` and SHALL NOT decode the IR.

#### Scenario: Digest mismatch
- **WHEN** an envelope's SHA-256 does not match its decoded bytes
- **THEN** admission fails with `-32602`, the IR is not decoded, and the session stays usable

#### Scenario: Pinned model-transport cases
- **WHEN** every case in the pinned `model-transport.cases.json` is run through admission
- **THEN** each case gets its expected verdict

### Requirement: The protocol layer is AOT-safe
The MEP layer SHALL publish in the Native AOT `morphir` executable with no trim or AOT warning and no suppression. It SHALL NOT use reflection-based serialization or load code at runtime.

#### Scenario: AOT publish
- **WHEN** a test application that runs a full in-memory session through the MEP layer is published with Native AOT
- **THEN** the publish has no IL warning, and the session gives the same messages as under JIT
