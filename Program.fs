open ICSharpCode.SharpZipLib.Zip
open Mono.Cecil
open System
open System.IO
open Argu

type ZipTree =
    | File of name : string * size : int64
    | Directory of name : string * list<ZipTree>


module ZipTree =

    let rec add (path : list<string>) (size : int64) (t : list<ZipTree>) =
        match t with
        | [] ->
            match path with
            | [] -> t
            | [file] -> [File(file, size)]
            | h :: t -> [Directory(h, add t size [])]
        | many ->
            match path with
            | [] -> t
            | newName :: rest ->   
                let mutable found = false
                let newEntries =
                    many |> List.map (fun existing ->
                        match existing with
                        | Directory(dir, contents) when newName = dir ->   
                            found <- true
                            Directory(dir, add rest size contents)
                        | Directory _ ->
                            existing
                        | File(file,_) ->
                            if file = newName then failwith "File already exists"
                            existing
                    )

                if found then
                    newEntries
                else
                    match rest with
                    | [] -> File(newName, size) :: many
                    | _ -> Directory(newName, add rest size [])::many

    let ofArchive (a : ZipFile) =
        let names = 
            List.init (int a.Count) (fun i -> a.[i])
            |> List.map (fun s -> 
                let path = s.Name.TrimEnd('/').Split('/') |> Array.toList
                
                path, s.Size
            )

        let mutable tree = []

        for (n, size) in names do
            tree <- add n size tree

        tree



type Architecture =
    | X86
    | X64
    | Arm64

type OS =
    | MacOS
    | Linux
    | Windows

type Command =
    | Info of files : list<string>
    | Unpack of dir : string * arch : Architecture * os : OS * removeZip : bool

type CliArguments =
    | [<CliPrefix(CliPrefix.None)>] Info of files : string list
    | [<CliPrefix(CliPrefix.None)>] Unpack of dir : string * arch : string * os : string
    | [<AltCommandLine("-r")>] Remove_Zip

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Info _ -> "Display information about native dependencies in .dll or .nupkg files"
            | Unpack _ -> "Unpack native dependencies to a directory for a specific architecture and OS"
            | Remove_Zip -> "Remove the native.zip from DLLs after unpacking"

module ArgumentParser =
    let parseArchitecture (str : string) =
        match str.ToLower() with
        | "x86" -> Some X86
        | "x64" -> Some X64
        | "arm64" -> Some Arm64
        | _ -> None

    let parseOS (str : string) =
        match str.ToLower() with
        | "macos" | "osx" | "darwin" -> Some MacOS
        | "linux" -> Some Linux
        | "windows" | "win" -> Some Windows
        | _ -> None

    let toCommand (args : ParseResults<CliArguments>) : Result<Command, string> =
        try
            let allArgs = args.GetAllResults()
            let removeZip = args.Contains Remove_Zip

            match allArgs |> List.filter (function Remove_Zip -> false | _ -> true) with
            | [CliArguments.Info files] ->
                Ok (Command.Info files)
            | [CliArguments.Unpack(dir, archStr, osStr)] ->
                match parseArchitecture archStr, parseOS osStr with
                | Some arch, Some os ->
                    Ok (Command.Unpack(dir, arch, os, removeZip))
                | None, _ ->
                    Error (sprintf "Invalid architecture: %s (valid: x86, x64, arm64)" archStr)
                | _, None ->
                    Error (sprintf "Invalid OS: %s (valid: macos, linux, windows)" osStr)
            | [] ->
                Error "No command specified"
            | _ ->
                Error "Multiple commands specified. Please use only one command."
        with ex ->
            Error ex.Message

[<EntryPoint>]
let main args =
    let parser = ArgumentParser.Create<CliArguments>(programName = "NativeTest")

    try
        let parseResults = parser.Parse(args)

        match ArgumentParser.toCommand parseResults with
        | Error err ->
            printfn "Error: %s" err
            printfn "%s" (parser.PrintUsage())
            -1
        | Ok command ->
            match command with
            | Command.Info files ->
                let str (size : int64) =
                    if size >= 1048576 then sprintf "%.2f MB" (float size / 1048576.0)
                    else if size >= 1024 then sprintf "%.2f KB" (float size / 1024.0)
                    else sprintf "%d B" size

                for file in files do
                    if Path.GetExtension(file).ToLower() = ".nupkg" then
                        use s = File.OpenRead file
                        use pkg = new ZipFile(s)

                        let dlls =
                            List.init (int pkg.Count) (fun i -> pkg.[i])
                            |> List.filter (fun n -> n.Name.EndsWith ".dll")

                        printfn "%s:" (Path.GetFileName file)
                        for e in dlls do
                            printfn "  %s:" e.Name

                            use stream =
                                use s = pkg.GetInputStream(e)
                                let ms = new MemoryStream()
                                s.CopyTo ms
                                ms.Position <- 0L
                                ms

                            use ass = AssemblyDefinition.ReadAssembly(stream)
                            let nativeZip =
                                ass.MainModule.Resources
                                |> Seq.tryFind (fun r -> r.Name.Trim() = "native.zip")

                            match nativeZip with
                            | Some (:? EmbeddedResource as zip) ->
                                use archive = new ZipFile(zip.GetResourceStream())

                                let tree = ZipTree.ofArchive archive
                                let rec print (indent : string) (tree : list<ZipTree>) =
                                    for t in tree do
                                        match t with
                                        | File(name, size) ->
                                            printfn "%s%s: %s" indent name (str size)
                                        | Directory(name, contents) ->
                                            printfn "%s%s/" indent name
                                            print (indent + "  ") contents

                                printfn "    native.zip/"
                                print "      " tree
                            | _ ->
                                printfn "    no native dependencies"

                        ()


                    else
                        printfn "%s:" (Path.GetFileName file)
                        try
                            use ass = AssemblyDefinition.ReadAssembly(file)
                            let nativeZip =
                                ass.MainModule.Resources
                                |> Seq.tryFind (fun r -> r.Name.Trim() = "native.zip")
                            match nativeZip with
                            | Some (:? EmbeddedResource as zip) ->
                                use archive = new ZipFile(zip.GetResourceStream())

                                let tree = ZipTree.ofArchive archive
                                let rec print (indent : string) (tree : list<ZipTree>) =
                                    for t in tree do
                                        match t with
                                        | File(name, size) ->
                                            printfn "%s%s: %s" indent name (str size)
                                        | Directory(name, contents) ->
                                            printfn "%s%s/" indent name
                                            print (indent + "  ") contents

                                printfn "  native.zip/"
                                print "    " tree
                            | _ ->
                                printfn "  no native dependencies"
                        with _ ->
                            printfn "  not an assembly"
                0
            | Command.Unpack(dir, arch, os, removeZip) ->
                let mutable unpacked = 0
                let dlls = Directory.GetFiles(dir, "*.dll")
                for file in dlls do
                    let name = Path.GetFileName file

                    // Check if PDB exists
                    let pdbFile = Path.ChangeExtension(file, ".pdb")
                    let hasPdb = File.Exists pdbFile

                    let mutable entriesToExtract = []

                    // Read assembly and get all data we need in memory
                    let zipData =
                        try
                            let readerParams = Mono.Cecil.ReaderParameters(ReadSymbols = false)
                            let resolver = new Mono.Cecil.DefaultAssemblyResolver()
                            resolver.AddSearchDirectory(Path.GetDirectoryName(file))
                            readerParams.AssemblyResolver <- resolver

                            use ass = AssemblyDefinition.ReadAssembly(file, readerParams)
                            let nativeZip =
                                ass.MainModule.Resources
                                |> Seq.tryFind (fun r -> r.Name.Trim() = "native.zip")
                            match nativeZip with
                            | Some (:? EmbeddedResource as zip) ->
                                use stream = zip.GetResourceStream()
                                let ms = new MemoryStream()
                                stream.CopyTo ms
                                Some (ms.ToArray())
                            | _ -> None
                        with
                        | :? BadImageFormatException -> None

                    // Process the zip data if we found it
                    match zipData with
                    | Some data ->
                        use ms = new MemoryStream(data)
                        use archive = new ZipFile(ms)

                        let osName =
                            match os with
                            | OS.MacOS -> "mac"
                            | OS.Linux -> "linux"
                            | OS.Windows -> "windows"

                        let archName =
                            match arch with
                            | Architecture.Arm64 -> "ARM64"
                            | Architecture.X64 -> "AMD64"
                            | Architecture.X86 -> "x86"

                        let prefix = $"{osName}/{archName}/"

                        let entries =
                            List.init (int archive.Count) (fun i -> archive.[i])
                            |> List.filter (fun e -> e.Name.StartsWith prefix)

                        if not (List.isEmpty entries) then
                            printfn "%s" name

                            // Extract entries to memory
                            for e in entries do
                                let cleanName = e.Name.Substring(prefix.Length).TrimStart('/')
                                printfn "  %s" cleanName

                                use src = archive.GetInputStream(e)
                                let ms = new MemoryStream()
                                src.CopyTo ms
                                entriesToExtract <- (cleanName, ms.ToArray()) :: entriesToExtract

                            // Write the extracted files
                            for (cleanName, fileData) in entriesToExtract do
                                let outFile = Path.Combine(dir, cleanName.Replace('/', Path.DirectorySeparatorChar))
                                let outDir = Path.GetDirectoryName outFile
                                if not (Directory.Exists outDir) then
                                    Directory.CreateDirectory outDir |> ignore

                                if File.Exists outFile then
                                    File.Delete outFile

                                File.WriteAllBytes(outFile, fileData)
                                unpacked <- unpacked + 1

                            // Remove native.zip from assembly if requested
                            if removeZip then
                                let pdbBackup =
                                    if hasPdb then
                                        let backup = pdbFile + ".bak"
                                        File.Copy(pdbFile, backup, true)
                                        Some backup
                                    else
                                        None

                                try
                                    System.GC.Collect()
                                    System.GC.WaitForPendingFinalizers()

                                    let tempFile = file + ".tmp"

                                    do
                                        let readerParams = Mono.Cecil.ReaderParameters(ReadSymbols = false)
                                        let resolver = new Mono.Cecil.DefaultAssemblyResolver()
                                        resolver.AddSearchDirectory(Path.GetDirectoryName(file))
                                        readerParams.AssemblyResolver <- resolver

                                        use ass = AssemblyDefinition.ReadAssembly(file, readerParams)

                                        let nativeZip =
                                            ass.MainModule.Resources
                                            |> Seq.tryFind (fun r -> r.Name.Trim() = "native.zip")
                                        match nativeZip with
                                        | Some zip ->
                                            ass.MainModule.Resources.Remove zip |> ignore
                                            printfn "  removed native.zip"
                                        | None -> ()

                                        let writerParams = Mono.Cecil.WriterParameters(WriteSymbols = false)
                                        ass.Write(tempFile, writerParams)

                                    System.GC.Collect()
                                    System.GC.WaitForPendingFinalizers()

                                    File.Delete(file)
                                    File.Move(tempFile, file)

                                    match pdbBackup with
                                    | Some backup ->
                                        if File.Exists pdbFile then
                                            File.Delete pdbFile
                                        File.Move(backup, pdbFile)
                                    | None -> ()

                                with ex ->
                                    let tempFile = file + ".tmp"
                                    if File.Exists tempFile then
                                        File.Delete tempFile

                                    match pdbBackup with
                                    | Some backup ->
                                        if File.Exists backup then
                                            if File.Exists pdbFile then
                                                File.Delete pdbFile
                                            File.Move(backup, pdbFile)
                                    | None -> ()
                                    reraise()

                    | None -> ()

                printfn "unpacked %d files" unpacked
                0
    with ex ->
        printfn "Error: %s" ex.Message
        -1