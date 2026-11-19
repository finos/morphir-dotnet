// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0

namespace Morphir.Diagrams.Raster

open System
open System.IO
open System.IO.Compression

/// PNG, baseline JPEG and animated GIF encoders. Each writes the same bytes for the same pixels on every platform.
[<RequireQualifiedAccess>]
module internal Encoders =
    // PNG ------------------------------------------------------------------------------------------------------------

    let private crcTable =
        Array.init 256 (fun n ->
            let mutable c = uint32 n
            for _ in 0..7 do
                c <- if c &&& 1u <> 0u then 0xEDB88320u ^^^ (c >>> 1) else c >>> 1
            c)

    let private crc (bytes: byte[]) (offset: int) (count: int) =
        let mutable c = 0xFFFFFFFFu
        for index in offset .. offset + count - 1 do
            c <- crcTable[int ((c ^^^ uint32 bytes[index]) &&& 0xFFu)] ^^^ (c >>> 8)
        c ^^^ 0xFFFFFFFFu

    let private big (output: Stream) (value: uint32) =
        output.WriteByte(byte (value >>> 24))
        output.WriteByte(byte (value >>> 16))
        output.WriteByte(byte (value >>> 8))
        output.WriteByte(byte value)

    let private chunk (output: Stream) (kind: string) (data: byte[]) =
        big output (uint32 data.Length)
        let body = Array.append (Text.Encoding.ASCII.GetBytes kind) data
        output.Write(body, 0, body.Length)
        big output (crc body 0 body.Length)

    /// An 8-bit RGBA PNG; each row uses the Sub filter and zlib at a fixed level.
    let png (canvas: Canvas) =
        use output = new MemoryStream()
        output.Write([| 137uy; 80uy; 78uy; 71uy; 13uy; 10uy; 26uy; 10uy |], 0, 8)
        use header = new MemoryStream()
        big header (uint32 canvas.Width)
        big header (uint32 canvas.Height)
        header.Write([| 8uy; 6uy; 0uy; 0uy; 0uy |], 0, 5)
        chunk output "IHDR" (header.ToArray())
        let stride = canvas.Width * 4
        let raw = Array.zeroCreate ((stride + 1) * canvas.Height)

        for row in 0 .. canvas.Height - 1 do
            let at = row * (stride + 1)
            raw[at] <- 1uy

            for index in 0 .. stride - 1 do
                let current = canvas.Pixels[row * stride + index]
                let left = if index >= 4 then canvas.Pixels[row * stride + index - 4] else 0uy
                raw[at + 1 + index] <- current - left

        use compressed = new MemoryStream()

        do
            use zlib = new ZLibStream(compressed, CompressionLevel.Optimal, true)
            zlib.Write(raw, 0, raw.Length)

        chunk output "IDAT" (compressed.ToArray())
        chunk output "IEND" [||]
        output.ToArray()

    // JPEG -----------------------------------------------------------------------------------------------------------
    // The tables and the codec structure follow mizchi/image@0.4.3 (Apache-2.0), as diagrams.mbt adapts them; the DCT
    // uses a pinned integer basis and the colour conversion is exact integer arithmetic.

    let private zigzag =
        [| 0; 1; 8; 16; 9; 2; 3; 10; 17; 24; 32; 25; 18; 11; 4; 5; 12; 19; 26; 33; 40; 48; 41; 34; 27; 20; 13; 6; 7; 14; 21; 28
           35; 42; 49; 56; 57; 50; 43; 36; 29; 22; 15; 23; 30; 37; 44; 51; 58; 59; 52; 45; 38; 31; 39; 46; 53; 60; 61; 54; 47; 55; 62; 63 |]

    let private luminance =
        [| 16; 11; 10; 16; 24; 40; 51; 61; 12; 12; 14; 19; 26; 58; 60; 55; 14; 13; 16; 24; 40; 57; 69; 56; 14; 17; 22; 29; 51; 87; 80; 62
           18; 22; 37; 56; 68; 109; 103; 77; 24; 35; 55; 64; 81; 104; 113; 92; 49; 64; 78; 87; 103; 121; 120; 101; 72; 92; 95; 98; 112; 100; 103; 99 |]

    let private chrominance = Array.append [| 17; 18; 24; 47; 99; 99; 99; 99; 18; 21; 26; 66; 99; 99; 99; 99; 24; 26; 56; 99; 99; 99; 99; 99; 47; 66 |] (Array.create 38 99)

    let private basis =
        [|
            [| 5793; 5793; 5793; 5793; 5793; 5793; 5793; 5793 |]
            [| 8035; 6811; 4551; 1598; -1598; -4551; -6811; -8035 |]
            [| 7568; 3135; -3135; -7568; -7568; -3135; 3135; 7568 |]
            [| 6811; -1598; -8035; -4551; 4551; 8035; 1598; -6811 |]
            [| 5793; -5793; -5793; 5793; 5793; -5793; -5793; 5793 |]
            [| 4551; -8035; 1598; 6811; -6811; -1598; 8035; -4551 |]
            [| 3135; -7568; 7568; -3135; -3135; 7568; -7568; 3135 |]
            [| 1598; -4551; 6811; -8035; 8035; -6811; 4551; -1598 |]
        |]

    let private dcLumaBits = [| 0; 1; 5; 1; 1; 1; 1; 1; 1; 0; 0; 0; 0; 0; 0; 0 |]
    let private dcChromaBits = [| 0; 3; 1; 1; 1; 1; 1; 1; 1; 1; 1; 0; 0; 0; 0; 0 |]
    let private acLumaBits = [| 0; 2; 1; 3; 3; 2; 4; 3; 5; 5; 4; 4; 0; 0; 1; 0x7D |]
    let private acChromaBits = [| 0; 2; 1; 2; 4; 4; 3; 4; 7; 5; 4; 4; 0; 1; 2; 0x77 |]
    let private dcValues = [| 0..11 |]

    let private acLumaValues =
        [| 0x01; 0x02; 0x03; 0x00; 0x04; 0x11; 0x05; 0x12; 0x21; 0x31; 0x41; 0x06; 0x13; 0x51; 0x61; 0x07; 0x22; 0x71; 0x14; 0x32; 0x81; 0x91; 0xA1; 0x08
           0x23; 0x42; 0xB1; 0xC1; 0x15; 0x52; 0xD1; 0xF0; 0x24; 0x33; 0x62; 0x72; 0x82; 0x09; 0x0A; 0x16; 0x17; 0x18; 0x19; 0x1A; 0x25; 0x26; 0x27; 0x28
           0x29; 0x2A; 0x34; 0x35; 0x36; 0x37; 0x38; 0x39; 0x3A; 0x43; 0x44; 0x45; 0x46; 0x47; 0x48; 0x49; 0x4A; 0x53; 0x54; 0x55; 0x56; 0x57; 0x58; 0x59
           0x5A; 0x63; 0x64; 0x65; 0x66; 0x67; 0x68; 0x69; 0x6A; 0x73; 0x74; 0x75; 0x76; 0x77; 0x78; 0x79; 0x7A; 0x83; 0x84; 0x85; 0x86; 0x87; 0x88; 0x89
           0x8A; 0x92; 0x93; 0x94; 0x95; 0x96; 0x97; 0x98; 0x99; 0x9A; 0xA2; 0xA3; 0xA4; 0xA5; 0xA6; 0xA7; 0xA8; 0xA9; 0xAA; 0xB2; 0xB3; 0xB4; 0xB5; 0xB6
           0xB7; 0xB8; 0xB9; 0xBA; 0xC2; 0xC3; 0xC4; 0xC5; 0xC6; 0xC7; 0xC8; 0xC9; 0xCA; 0xD2; 0xD3; 0xD4; 0xD5; 0xD6; 0xD7; 0xD8; 0xD9; 0xDA; 0xE1; 0xE2
           0xE3; 0xE4; 0xE5; 0xE6; 0xE7; 0xE8; 0xE9; 0xEA; 0xF1; 0xF2; 0xF3; 0xF4; 0xF5; 0xF6; 0xF7; 0xF8; 0xF9; 0xFA |]

    let private acChromaValues =
        [| 0x00; 0x01; 0x02; 0x03; 0x11; 0x04; 0x05; 0x21; 0x31; 0x06; 0x12; 0x41; 0x51; 0x07; 0x61; 0x71; 0x13; 0x22; 0x32; 0x81; 0x08; 0x14; 0x42; 0x91
           0xA1; 0xB1; 0xC1; 0x09; 0x23; 0x33; 0x52; 0xF0; 0x15; 0x62; 0x72; 0xD1; 0x0A; 0x16; 0x24; 0x34; 0xE1; 0x25; 0xF1; 0x17; 0x18; 0x19; 0x1A; 0x26
           0x27; 0x28; 0x29; 0x2A; 0x35; 0x36; 0x37; 0x38; 0x39; 0x3A; 0x43; 0x44; 0x45; 0x46; 0x47; 0x48; 0x49; 0x4A; 0x53; 0x54; 0x55; 0x56; 0x57; 0x58
           0x59; 0x5A; 0x63; 0x64; 0x65; 0x66; 0x67; 0x68; 0x69; 0x6A; 0x73; 0x74; 0x75; 0x76; 0x77; 0x78; 0x79; 0x7A; 0x82; 0x83; 0x84; 0x85; 0x86; 0x87
           0x88; 0x89; 0x8A; 0x92; 0x93; 0x94; 0x95; 0x96; 0x97; 0x98; 0x99; 0x9A; 0xA2; 0xA3; 0xA4; 0xA5; 0xA6; 0xA7; 0xA8; 0xA9; 0xAA; 0xB2; 0xB3; 0xB4
           0xB5; 0xB6; 0xB7; 0xB8; 0xB9; 0xBA; 0xC2; 0xC3; 0xC4; 0xC5; 0xC6; 0xC7; 0xC8; 0xC9; 0xCA; 0xD2; 0xD3; 0xD4; 0xD5; 0xD6; 0xD7; 0xD8; 0xD9; 0xDA
           0xE2; 0xE3; 0xE4; 0xE5; 0xE6; 0xE7; 0xE8; 0xE9; 0xEA; 0xF2; 0xF3; 0xF4; 0xF5; 0xF6; 0xF7; 0xF8; 0xF9; 0xFA |]

    /// Code and length for each symbol of a canonical Huffman table.
    let private codes (bits: int[]) (values: int[]) =
        let code = Array.zeroCreate 256
        let length = Array.zeroCreate 256
        let mutable next = 0
        let mutable index = 0

        for size in 1..16 do
            for _ in 1 .. bits[size - 1] do
                code[values[index]] <- next
                length[values[index]] <- size
                next <- next + 1
                index <- index + 1

            next <- next <<< 1

        code, length

    /// A baseline 4:4:4 JFIF JPEG of the canvas composited onto an opaque background.
    let jpeg (canvas: Canvas) (quality: int) (background: Rgba) =
        let output = new MemoryStream()
        let bytes (values: int seq) = for value in values do output.WriteByte(byte value)
        let word (value: int) = bytes [ value >>> 8; value &&& 0xFF ]
        let scale = if quality < 50 then 5000 / quality else 200 - 2 * quality
        let table (basis: int[]) = basis |> Array.map (fun value -> (value * scale + 50) / 100 |> max 1 |> min 255)
        let lumaQ, chromaQ = table luminance, table chrominance

        bytes [ 0xFF; 0xD8; 0xFF; 0xE0 ]
        word 16
        bytes [ 0x4A; 0x46; 0x49; 0x46; 0x00; 1; 1; 0 ]
        word 1
        word 1
        bytes [ 0; 0 ]

        for id, q in [ 0, lumaQ; 1, chromaQ ] do
            bytes [ 0xFF; 0xDB ]
            word 67
            bytes [ id ]
            bytes [ for index in 0..63 -> q[zigzag[index]] ]

        bytes [ 0xFF; 0xC0 ]
        word 17
        bytes [ 8 ]
        word canvas.Height
        word canvas.Width
        bytes [ 3; 1; 0x11; 0; 2; 0x11; 1; 3; 0x11; 1 ]

        for kind, bits, values in [ 0x00, dcLumaBits, dcValues; 0x10, acLumaBits, acLumaValues; 0x01, dcChromaBits, dcValues; 0x11, acChromaBits, acChromaValues ] do
            bytes [ 0xFF; 0xC4 ]
            word (19 + values.Length)
            bytes [ kind ]
            bytes bits
            bytes values

        bytes [ 0xFF; 0xDA ]
        word 12
        bytes [ 3; 1; 0x00; 2; 0x11; 3; 0x11; 0; 63; 0 ]

        // Entropy-coded data.
        let mutable buffer = 0
        let mutable count = 0

        let emit (value: int) (size: int) =
            if size > 0 then
                buffer <- (buffer <<< size) ||| (value &&& ((1 <<< size) - 1))
                count <- count + size

                while count >= 8 do
                    let out = (buffer >>> (count - 8)) &&& 0xFF
                    output.WriteByte(byte out)
                    if out = 0xFF then output.WriteByte 0uy
                    count <- count - 8
                    buffer <- buffer &&& ((1 <<< count) - 1)

        let dcLuma, acLuma = codes dcLumaBits dcValues, codes acLumaBits acLumaValues
        let dcChroma, acChroma = codes dcChromaBits dcValues, codes acChromaBits acChromaValues

        let category (value: int) =
            let mutable magnitude = abs value
            let mutable bitsNeeded = 0
            while magnitude > 0 do
                magnitude <- magnitude >>> 1
                bitsNeeded <- bitsNeeded + 1
            bitsNeeded

        let encodeBlock (block: int[]) (q: int[]) (previous: int) ((dcCode: int[], dcLength: int[]), (acCode: int[], acLength: int[])) =
            let temp = Array.zeroCreate<int64> 64
            let coefficients = Array.zeroCreate<int> 64

            for u in 0..7 do
                for y in 0..7 do
                    let mutable sum = 0L
                    for x in 0..7 do sum <- sum + int64 (basis[u][x]) * int64 (block[y * 8 + x])
                    temp[u * 8 + y] <- sum

            for u in 0..7 do
                for v in 0..7 do
                    let mutable sum = 0L
                    for y in 0..7 do sum <- sum + int64 (basis[v][y]) * temp[u * 8 + y]
                    // Back to unit scale (2^28), then quantised with rounding half away from zero.
                    let divisor = 268435456L * int64 q[v * 8 + u]
                    let rounded = if sum >= 0L then (sum + divisor / 2L) / divisor else -((-sum + divisor / 2L) / divisor)
                    coefficients[v * 8 + u] <- int rounded

            let dc = coefficients[0]
            let difference = dc - previous
            let size = category difference
            emit dcCode[size] dcLength[size]
            emit (if difference < 0 then difference - 1 else difference) size
            let mutable run = 0

            for index in 1..63 do
                let value = coefficients[zigzag[index]]

                if value = 0 then
                    run <- run + 1
                else
                    while run > 15 do
                        emit acCode[0xF0] acLength[0xF0]
                        run <- run - 16

                    let size = category value
                    let symbol = (run <<< 4) ||| size
                    emit acCode[symbol] acLength[symbol]
                    emit (if value < 0 then value - 1 else value) size
                    run <- 0

            if run > 0 then emit acCode[0] acLength[0]
            dc

        let mutable previous = [| 0; 0; 0 |]
        let luma, cb, cr = Array.zeroCreate 64, Array.zeroCreate 64, Array.zeroCreate 64

        for blockY in 0 .. (canvas.Height + 7) / 8 - 1 do
            for blockX in 0 .. (canvas.Width + 7) / 8 - 1 do
                for y in 0..7 do
                    for x in 0..7 do
                        let px = min (canvas.Width - 1) (blockX * 8 + x)
                        let py = min (canvas.Height - 1) (blockY * 8 + y)
                        let at = (py * canvas.Width + px) * 4
                        let a = int canvas.Pixels[at + 3]
                        let over (channel: int) (back: int) = (channel * a + back * (255 - a) + 127) / 255
                        let r = over (int canvas.Pixels[at]) background.R
                        let g = over (int canvas.Pixels[at + 1]) background.G
                        let b = over (int canvas.Pixels[at + 2]) background.B
                        luma[y * 8 + x] <- ((19595 * r + 38470 * g + 7471 * b + 32768) >>> 16) - 128
                        cb[y * 8 + x] <- ((-11059 * r - 21709 * g + 32768 * b + 32768) >>> 16)
                        cr[y * 8 + x] <- ((32768 * r - 27439 * g - 5329 * b + 32768) >>> 16)

                previous[0] <- encodeBlock luma lumaQ previous[0] (dcLuma, acLuma)
                previous[1] <- encodeBlock cb chromaQ previous[1] (dcChroma, acChroma)
                previous[2] <- encodeBlock cr chromaQ previous[2] (dcChroma, acChroma)

        if count > 0 then emit ((1 <<< (8 - count)) - 1) (8 - count)
        bytes [ 0xFF; 0xD9 ]
        output.ToArray()

    // GIF ------------------------------------------------------------------------------------------------------------

    /// The fixed palette: a 6x7x6 colour cube and four greys, so every frame shares one deterministic table.
    let private palette =
        [|
            for r in 0..5 do
                for g in 0..6 do
                    for b in 0..5 do
                        yield r * 51, g * 255 / 6, b * 51
            for grey in [ 32; 96; 160; 224 ] do
                yield grey, grey, grey
        |]

    let private nearest (r: int) (g: int) (b: int) =
        let mutable best = 0
        let mutable distance = Int32.MaxValue

        for index in 0 .. palette.Length - 1 do
            let pr, pg, pb = palette[index]
            let d = (pr - r) * (pr - r) * 3 + (pg - g) * (pg - g) * 4 + (pb - b) * (pb - b) * 2
            if d < distance then
                distance <- d
                best <- index

        best

    /// An animated GIF of frames composited onto an opaque background, each shown for `delay` hundredths of a second,
    /// looping forever when `loop` is set.
    let gif (frames: Canvas list) (delay: int) (loop: bool) (background: Rgba) =
        let output = new MemoryStream()
        let first = frames.Head
        let bytes (values: int seq) = for value in values do output.WriteByte(byte value)
        let little (value: int) = bytes [ value &&& 0xFF; (value >>> 8) &&& 0xFF ]
        output.Write(Text.Encoding.ASCII.GetBytes "GIF89a", 0, 6)
        little first.Width
        little first.Height
        bytes [ 0xF7; 0; 0 ]

        for r, g, b in palette do
            bytes [ r; g; b ]

        for _ in palette.Length .. 255 do
            bytes [ 0; 0; 0 ]

        if loop then
            bytes [ 0x21; 0xFF; 11 ]
            output.Write(Text.Encoding.ASCII.GetBytes "NETSCAPE2.0", 0, 11)
            bytes [ 3; 1; 0; 0; 0 ]

        let cache = Collections.Generic.Dictionary<int, int>()

        for frame in frames do
            bytes [ 0x21; 0xF9; 4; 0x04 ]
            little delay
            bytes [ 0; 0; 0x2C ]
            little 0
            little 0
            little frame.Width
            little frame.Height
            bytes [ 0; 8 ]

            // Indices of the frame's pixels.
            let indices =
                Array.init (frame.Width * frame.Height) (fun pixel ->
                    let at = pixel * 4
                    let a = int frame.Pixels[at + 3]
                    let over (channel: int) (back: int) = (channel * a + back * (255 - a) + 127) / 255
                    let r = over (int frame.Pixels[at]) background.R
                    let g = over (int frame.Pixels[at + 1]) background.G
                    let b = over (int frame.Pixels[at + 2]) background.B
                    let key = (r <<< 16) ||| (g <<< 8) ||| b

                    match cache.TryGetValue key with
                    | true, found -> found
                    | _ ->
                        let found = nearest r g b
                        cache[key] <- found
                        found)

            // LZW with 8-bit roots, clear code 256, end code 257, codes up to 12 bits.
            let packed = new MemoryStream()
            let mutable bitBuffer = 0
            let mutable bitCount = 0
            let mutable codeSize = 9

            let write (code: int) =
                bitBuffer <- bitBuffer ||| (code <<< bitCount)
                bitCount <- bitCount + codeSize

                while bitCount >= 8 do
                    packed.WriteByte(byte (bitBuffer &&& 0xFF))
                    bitBuffer <- bitBuffer >>> 8
                    bitCount <- bitCount - 8

            let table = Collections.Generic.Dictionary<int, int>()
            let mutable nextCode = 258
            write 256
            let mutable prefix = indices[0]

            for index in 1 .. indices.Length - 1 do
                let symbol = indices[index]
                let key = (prefix <<< 8) ||| symbol

                match table.TryGetValue key with
                | true, code -> prefix <- code
                | _ ->
                    write prefix

                    if nextCode < 4096 then
                        table[key] <- nextCode
                        if nextCode = (1 <<< codeSize) && codeSize < 12 then codeSize <- codeSize + 1
                        nextCode <- nextCode + 1
                    else
                        write 256
                        table.Clear()
                        nextCode <- 258
                        codeSize <- 9

                    prefix <- symbol

            write prefix
            write 257
            if bitCount > 0 then packed.WriteByte(byte (bitBuffer &&& 0xFF))
            let data = packed.ToArray()
            let mutable offset = 0

            while offset < data.Length do
                let size = min 255 (data.Length - offset)
                bytes [ size ]
                output.Write(data, offset, size)
                offset <- offset + size

            bytes [ 0 ]

        bytes [ 0x3B ]
        output.ToArray()
