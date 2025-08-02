module KPX.Pubmed_RSS.Feeds

open System
open System.Text
open System.IO
open System.Xml
open System.Xml.Linq


type RssSourceType =
    | Url of string
    | File of string

type FeedInfo =
    {
        /// 绑定到哪个路径名上，需要唯一检查
        /// 以/开始
        BindPath: string
        /// Feed的介绍
        FeedText: string
        /// Feed的标题
        FeedTitle: string
        /// Pubmed 来源RSS信息
        PubmedRssSource: RssSourceType
    }

let feeds: FeedInfo list =
    let debug = false

    if not debug then
        [ { BindPath = "analbiochem"
            FeedText = "期刊：Analytical Biochemistry"
            FeedTitle = "Pubmed：Analytical Biochemistry"
            PubmedRssSource =
              Url
                  "https://pubmed.ncbi.nlm.nih.gov/rss/journals/0370535/?limit=100&name=Anal%20Biochem&utm_campaign=journals" }
          { BindPath = "glioblastoma"
            FeedText = "Pubmed：glioblastoma"
            FeedTitle = "Pubmed：glioblastoma"
            PubmedRssSource =
              Url
                  "https://pubmed.ncbi.nlm.nih.gov/rss/search/1Zw2sdRS3tjCese8aejBrMjk-hy1RfSWGwCwGPXttfyEFplsgV/?limit=100&utm_campaign=pubmed-2&fc=20250727022311" }

          ]
    else
        [ { BindPath = "/test1"
            FeedText = "FSD1"
            FeedTitle = "FSD1"
            PubmedRssSource = File(Path.Join(__SOURCE_DIRECTORY__, ".utils", "Sample.xml")) }
          { BindPath = "/test2"
            FeedText = "JAMA"
            FeedTitle = "JAMA"
            PubmedRssSource = File(Path.Join(__SOURCE_DIRECTORY__, ".utils", "Sample_JAMA.xml")) } ]


type private UTF8StringWriter(sb: StringBuilder) =
    inherit StringWriter(sb)

let generateOpml (externalUrl: string) =
    let outlineElements =
        feeds
        |> List.map (fun feed ->
            XElement(
                XName.Get "outline",
                XAttribute(XName.Get "type", "rss"),
                XAttribute(XName.Get "text", feed.FeedText),
                XAttribute(XName.Get "title", feed.FeedTitle),
                match feed.PubmedRssSource with
                | Url _ -> XAttribute(XName.Get "xmlUrl", Uri(Uri(externalUrl), feed.BindPath))
                | File path -> XAttribute(XName.Get "xmlUrl", Uri(path).AbsoluteUri)
            ))

    let outlineGroup =
        XElement(
            XName.Get "outline",
            XAttribute(XName.Get "text", "PubmedRss-OPML"),
            XAttribute(XName.Get "title", "PubmedRss-OPML"),
            outlineElements
        )

    let body = XElement(XName.Get "body", outlineElements)

    let head = XElement(XName.Get "head", XElement(XName.Get "title", "Pubmed-RSS"))

    let opml =
        XElement(XName.Get "opml", XAttribute(XName.Get "version", "2.0"), head, body)

    let doc = XDocument(opml)
    doc.Declaration <- XDeclaration("1.0", "utf-8", "yes")

    use ms = new MemoryStream()
    use sw = new StreamWriter(ms, UTF8Encoding(false), AutoFlush = true)
    doc.Save(sw)

    ms.ToArray()

do
    // 检查 BindPath 唯一性
    ()
