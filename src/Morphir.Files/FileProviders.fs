namespace Morphir.Files

open System
open System.Collections
open System.Collections.Generic
open System.IO
open System.Text
open Microsoft.Extensions.FileProviders
open Microsoft.Extensions.Primitives

/// <summary>A file of a tree, as an <c>IFileInfo</c>. Its length is worked out when it is first asked for.</summary>
[<Sealed>]
type internal TreeFileInfo(name: string, location: string, file: VFile) =
    /// Opens the content, raising the read failure with the location as an IOException, as a file provider does.
    let openContent () : Stream =
        match file.Content with
        | None -> new MemoryStream(Array.empty<byte>, false) :> Stream
        | Some content ->
            match VFileContent.openRead content with
            | Result.Ok stream -> stream
            | Result.Error (ContentError.ReadFailed (_, cause)) ->
                raise (IOException(ContentError.describe (ContentError.ReadFailed (Some location, cause)), cause))

    let length =
        lazy
            (match file.Content with
             | None -> 0L
             | Some (VFileContent.Text text) -> int64 (UTF8Encoding(false).GetByteCount text)
             | Some (VFileContent.Bytes bytes) -> int64 bytes.Length
             | Some (VFileContent.Stream source) ->
                 match source.Length with
                 | Some known -> known
                 | None ->
                     use stream = openContent ()

                     if stream.CanSeek then
                         stream.Length
                     else
                         let buffer = Array.zeroCreate<byte> 81920
                         let mutable total = 0L
                         let mutable read = stream.Read(buffer, 0, buffer.Length)

                         while read > 0 do
                             total <- total + int64 read
                             read <- stream.Read(buffer, 0, buffer.Length)

                         total)

    interface IFileInfo with
        member _.Exists = true
        member _.Length = length.Force()
        member _.PhysicalPath = null
        member _.Name = name
        member _.LastModified = DateTimeOffset.MinValue
        member _.IsDirectory = false
        member _.CreateReadStream() = openContent ()

/// <summary>A directory of a tree, as an <c>IFileInfo</c> in a directory listing.</summary>
[<Sealed>]
type internal TreeDirectoryInfo(name: string) =
    interface IFileInfo with
        member _.Exists = true
        member _.Length = -1L
        member _.PhysicalPath = null
        member _.Name = name
        member _.LastModified = DateTimeOffset.MinValue
        member _.IsDirectory = true
        member _.CreateReadStream() = raise (InvalidOperationException("Cannot create a stream for the directory " + name + "."))

/// <summary>The entries of a directory of a tree.</summary>
[<Sealed>]
type internal TreeDirectoryContents(entries: IFileInfo list) =
    interface IDirectoryContents with
        member _.Exists = true

    interface IEnumerable<IFileInfo> with
        member _.GetEnumerator() = (entries :> IEnumerable<IFileInfo>).GetEnumerator()

    interface IEnumerable with
        member _.GetEnumerator() = (entries :> IEnumerable).GetEnumerator()

/// <summary>
/// A tree as an <c>IFileProvider</c>. It indexes the tree's locations once, when it is made; trees do not change, so the
/// index does not either.
/// </summary>
[<Sealed>]
type internal TreeFileProvider(tree: VFileTree) =
    let files = Dictionary<string, IFileInfo>(StringComparer.Ordinal)
    /// For each directory location ("" is the root): the names of its files and of its subdirectories, in tree order.
    let listings = Dictionary<string, ResizeArray<string> * ResizeArray<string>>(StringComparer.Ordinal)

    /// The parent location and the name of a location.
    let split (location: string) =
        match location.LastIndexOf '/' with
        | -1 -> "", location
        | index -> location.Substring(0, index), location.Substring(index + 1)

    /// A location the provider can give: inside the tree, not the root, and not climbing out with "..".
    let usable (location: string) =
        location <> "" && not (VFileTree.isRooted location) && not (Array.contains ".." (location.Split '/'))

    let rec addDirectory (location: string) =
        if not (listings.ContainsKey location) then
            listings[location] <- (ResizeArray(), ResizeArray())

            if location <> "" then
                let parent, name = split location
                addDirectory parent
                (snd listings[parent]).Add name

    /// A later file at the same location replaces the earlier one, as in VFileTree.toFileMap.
    let addFile (location: string) (file: VFile) =
        let parent, name = split location
        addDirectory parent

        if not (files.ContainsKey location) then
            (fst listings[parent]).Add name

        files[location] <- TreeFileInfo(name, location, file)

    let rec directoryLocations (basePath: string) (content: TreeNode list) : string list =
        content
        |> List.collect (function
            | TreeNode.File _ -> []
            | TreeNode.Directory subtree ->
                let location = VFileTree.resolvePath basePath subtree.Path
                location :: directoryLocations location subtree.Content)

    do
        addDirectory ""

        for location in directoryLocations "" tree.Content do
            if usable location then addDirectory location

        for location, file in VFileTree.locatedFiles tree do
            if usable location then addFile location file

    /// The location of a provider subpath, with either separator and an optional leading separator, or None when it
    /// climbs out with "..".
    let locationOf (subpath: string) =
        if isNull subpath then
            Some ""
        else
            let segments = VFileTree.pathSegments subpath
            if List.contains ".." segments then None else Some(String.Join("/", segments))

    let join (directory: string) (name: string) =
        if directory = "" then name else directory + "/" + name

    interface IFileProvider with
        member _.GetFileInfo(subpath: string) : IFileInfo =
            match locationOf subpath with
            | Some location when files.ContainsKey location -> files[location]
            | _ -> NotFoundFileInfo(if isNull subpath then "" else subpath) :> IFileInfo

        member _.GetDirectoryContents(subpath: string) : IDirectoryContents =
            match locationOf subpath with
            | Some location when listings.ContainsKey location ->
                let fileNames, directoryNames = listings[location]

                TreeDirectoryContents(
                    [ for name in fileNames -> files[join location name]
                      for name in directoryNames -> TreeDirectoryInfo(name) :> IFileInfo ]
                )
                :> IDirectoryContents
            | _ -> NotFoundDirectoryContents.Singleton :> IDirectoryContents

        member _.Watch(_: string) : IChangeToken = NullChangeToken.Singleton :> IChangeToken

/// <summary>
/// Converts between virtual file trees and Microsoft <c>IFileProvider</c>s, so a tree can go wherever a provider is
/// accepted, and a tree can be built from any provider (physical, embedded, composite) without reading its files.
/// </summary>
[<RequireQualifiedAccess>]
module VFileProviders =
    /// <summary>
    /// A file provider over a tree. A path is a file's location in the tree (see <see cref="VFileTree"/>), with either
    /// separator and an optional leading one; lookups are case-sensitive. A file's stream opens its content, its length
    /// is its byte count, and it has no physical path or modification time (<c>DateTimeOffset.MinValue</c>). A
    /// directory, a missing path and a path that climbs out with <c>..</c> are not files. Listing a directory gives its
    /// files and then its subdirectories, in tree order; the empty path lists the top level. Watching gives a change
    /// token that never fires. Files with a rooted location are left out.
    /// </summary>
    /// <param name="tree">The tree to provide</param>
    [<CompiledName("AsFileProvider")>]
    let asFileProvider (tree: VFileTree) : IFileProvider = TreeFileProvider(tree) :> IFileProvider

    /// <summary>
    /// A tree of every file and directory that a provider lists, from its root down. Each file keeps its location below
    /// the root, with forward slashes, and its content is a <c>VFileContent.Stream</c> that opens the provider's file
    /// when it is read, so building the tree reads no content. As with <see cref="VFileTree.loadFromDisk"/>, each
    /// directory lists its files first and then its subdirectories, both sorted by name, and empty directories are kept.
    /// The tree's <c>Path</c> is ".", and its configuration and metadata are empty.
    /// </summary>
    /// <param name="provider">The provider to read</param>
    [<CompiledName("OfFileProvider")>]
    let ofFileProvider (provider: IFileProvider) : VFileTree =
        let byName (entries: IFileInfo list) =
            entries |> List.sortWith (fun first second -> String.CompareOrdinal(first.Name, second.Name))

        // The root is "/": the embedded providers of .NET do not list the empty path, and every provider lists "/".
        let rec load (directory: string) : TreeNode list =
            let contents = provider.GetDirectoryContents directory

            if not contents.Exists then
                []
            else
                let entries = [ for entry in contents do if entry.Exists then entry ]
                let directories, files = entries |> List.partition _.IsDirectory

                let fileNodes =
                    byName files
                    |> List.map (fun info ->
                        let length = info.Length

                        let source =
                            if length >= 0L then
                                StreamSource((fun () -> info.CreateReadStream()), length)
                            else
                                StreamSource(fun () -> info.CreateReadStream())

                        TreeNode.File(VFile.ofStream info.Name source))

                let directoryNodes =
                    byName directories
                    |> List.map (fun info ->
                        let location = if directory = "/" then info.Name else directory + "/" + info.Name
                        TreeNode.Directory { VFileTree.empty with Path = info.Name; Content = load location })

                fileNodes @ directoryNodes

        { VFileTree.empty with Content = load "/" }
