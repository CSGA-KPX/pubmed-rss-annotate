module KPX.Pubmed_RSS.IFDatabase

open System
open System.Collections.Generic
open System.IO
open System.Xml
open System.Reflection


type JournalInfo =
    { Name: string; ISSNs: HashSet<string> }

type JCRInfo =
    { Name: string
      Abbr: string
      PISSN: string
      EISSN: string
      JIF: string
      JIF5: string
      Category: string }

let private logger = NLog.LogManager.GetLogger("IFDatabase")

let private getResFileStream resName =
    let assembly = Assembly.GetCallingAssembly()
    assembly.GetManifestResourceStream(resName)

let private pubmedJournals =
    let settings = XmlReaderSettings()
    settings.DtdProcessing <- DtdProcessing.Ignore

    use s = getResFileStream ("KPX.Pubmed_RSS.data.jourcache.xml")
    use reader = XmlReader.Create(s, settings)

    let journals = Dictionary<string, JournalInfo>(StringComparer.OrdinalIgnoreCase)
    let mutable currentName = None
    let mutable currentPIssn = None
    let mutable currentEIssn = None

    while reader.Read() do
        if reader.NodeType = XmlNodeType.Element then
            match reader.Name with
            | "Journal" ->
                currentName <- None
                currentPIssn <- None
                currentEIssn <- None
            | "Name" ->
                if reader.Read() && reader.NodeType = XmlNodeType.Text then
                    currentName <- Some(reader.Value.Trim())
            | "Issn" ->
                let issnType = reader.GetAttribute("type")

                if reader.Read() && reader.NodeType = XmlNodeType.Text then
                    match issnType with
                    | "print" -> currentPIssn <- Some(reader.Value.Trim())
                    | "electronic" -> currentEIssn <- Some(reader.Value.Trim())
                    | _ -> ()
            | _ -> () // ignore other elements
        elif reader.NodeType = XmlNodeType.EndElement && reader.Name = "Journal" then
            match currentName with
            | Some name ->
                if not <| journals.ContainsKey(name) then
                    journals.Add(name, { Name = name; ISSNs = HashSet<_>() })

                if currentPIssn.IsSome then
                    journals.[name].ISSNs.Add(currentPIssn.Value) |> ignore

                if currentEIssn.IsSome then
                    journals.[name].ISSNs.Add(currentEIssn.Value) |> ignore
            | None -> () // Skip incomplete Journal entries

    journals.AsReadOnly()

let private ifCache =
    let na = "N/A"
    let dict = Dictionary<string, JCRInfo>(40000, StringComparer.OrdinalIgnoreCase)

    use s = getResFileStream ("KPX.Pubmed_RSS.data.CopyofImpactFactor2024.csv")
    use sr = new StreamReader(s)
    sr.ReadLine() |> ignore // skip header

    while not sr.EndOfStream do
        let line = sr.ReadLine()
        let t = line.Split(',', 7)

        if t.Length <> 7 then
            failwithf $"数据验证失败 line = \r\n {line}"

        let o =
            { Name = t.[0]
              Abbr = t.[1]
              PISSN = t.[2]
              EISSN = t.[3]
              JIF = t.[4]
              JIF5 = t.[5]
              Category = t.[6] }

        if o.PISSN <> na then
            dict.TryAdd(o.PISSN, o) |> ignore

        if o.EISSN <> na then
            dict.TryAdd(o.EISSN, o) |> ignore

    dict.AsReadOnly()

let ifLookup (jName: string) =
    let succ, jInfo = pubmedJournals.TryGetValue(jName)

    if succ then

        let ret = jInfo.ISSNs |> Seq.tryFind (ifCache.ContainsKey)

        if ret.IsSome then
            let info = ifCache.[ret.Value]
            Some($"IF={info.JIF5} CAT={info.Category}")
        else
            logger.Error("没在ifCache中找到 {0}", jInfo)
            None
    else
        logger.Error("没在 pubmedJournals 中找到{0}", jName)
        None
