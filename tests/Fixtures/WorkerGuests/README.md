# Worker guests

Two WASM guests for the tests of `morphir extension worker` (OpenSpec change `add-wasm-extension-host`, task 4.1).
They are written for the tests of morphir-dotnet and are not a part of the pinned corpus of `../MepWasm`.

| File | What it does |
| --- | --- |
| `flood-output.wat` | Each call writes 10 MiB to WASI stdout and logs 10 MiB, then answers. It checks the bound of the guest output. |
| `workspace-then-spin.wat` | Each call asks for the private workspace of the session and then loops. It checks the interrupt of a worker. |

Each `.wasm` file is the conversion of the `.wat` file beside it by Wasmtime. `WorkerBuildIdentityTests` in
`tests/Morphir.Tooling.Tests` checks that each `.wasm` file is the conversion of its `.wat` file.

Users: `tests/Morphir.BlackBox.Tests` (`Integration/ExtensionWorker.feature`, `Integration/ExtensionWorkerCorpusTests.cs`)
and `tests/Morphir.Tooling.Tests` link the files.
