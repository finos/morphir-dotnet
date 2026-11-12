# Spec Delta

## ADDED Requirements

### Requirement: A host session applies one set of rules on every channel
A host session SHALL apply the same version negotiation, lifecycle, capability and model-transport rules whether its guest answers on a framed stream or takes one message per call. A request that the host refuses to send on one channel SHALL be refused on the other, with the same error.

#### Scenario: A single-message guest completes a session
- **WHEN** a host session runs `morphir.initialize`, one extension request and `morphir.shutdown` against a guest that takes one message per call
- **THEN** each request gets its response, and the negotiated session equals the one the same guest gives on a framed stream

#### Scenario: The same refusal on both channels
- **WHEN** a host session is asked to send `morphir.extension.capabilities` before `morphir.initialize`, once on a framed stream and once on a single-message channel
- **THEN** both refuse with `-32014`, and neither guest receives the request

#### Scenario: Process guests are unchanged
- **WHEN** the existing process-guest tests and pinned transcript replays run after the host session moves to a channel
- **THEN** every one passes with no change to its expectations

### Requirement: One call carries one message and at most one reply
For a single-message guest, the host SHALL pass each message as one complete UTF-8 JSON-RPC message with no `Content-Length` header, and SHALL accept at most one message in return. For a request, the return SHALL be the response with the same id. No return, a response with another id, or a message that is not a response SHALL fail the session.

#### Scenario: Request with no reply
- **WHEN** a single-message guest returns no bytes for a request
- **THEN** the session fails, and the host does not wait for a later reply

#### Scenario: Reply for another request
- **WHEN** a single-message guest returns a response whose id differs from the request's id
- **THEN** the session fails, and the response is not given to the caller

#### Scenario: Guest returns a notification
- **WHEN** a single-message guest returns a `morphir.progress` notification as the reply to a request
- **THEN** the session fails

### Requirement: A notification to a single-message guest returns nothing
When the host passes a notification to a single-message guest, the call SHALL return no bytes. Returned bytes SHALL fail the session. Whether a notification is passed at all is decided by the rules of the guest's runtime.

#### Scenario: Guest answers a notification
- **WHEN** a single-message guest returns bytes for `morphir.initialized`
- **THEN** the session fails

#### Scenario: Runtime withholds a notification
- **WHEN** the guest's runtime does not deliver notifications to that guest, and the host session sends `morphir.initialized`
- **THEN** the guest is not called, and the session continues as initialized

### Requirement: Cancelling a call to a single-message guest ends the session
A single-message guest runs one call at a time, so the host SHALL NOT send `$/cancelRequest` to it. When a pending request to such a guest is cancelled, the host SHALL stop the call, complete the request as cancelled and end the session. It SHALL NOT pass another message to that guest.

#### Scenario: Cancel during a call
- **WHEN** a request to a single-message guest is cancelled while the guest is inside the call
- **THEN** the request ends as cancelled and no `$/cancelRequest` message is passed to the guest
- **AND** a later request on that session is refused without a call
