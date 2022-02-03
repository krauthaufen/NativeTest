open ICSharpCode.SharpZipLib.Zip
open Mono.Cecil
open System.IO

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








[<EntryPoint>]
let main args =
    if args.Length <= 0 then
        printfn "usage: NativeTest <dllfile>"
        -1
    else

        let files = 
            args |> Array.filter File.Exists


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