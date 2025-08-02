module KPX.Pubmed_RSS.Program

open System
open System.Collections.Generic
open System.IO
open System.Text
open System.Text.RegularExpressions
open System.Net.Http
open System.Xml
open System.Xml.Linq
open System.Reflection
open System.ServiceModel.Syndication

open Falco
open Falco.Routing
open Microsoft.AspNetCore.Builder

open KPX.Pubmed_RSS.RateController
open Feeds


type FeedCacheHandler(feed: FeedInfo) as x =

    let updateSync = obj ()

    let mutable cache: SyndicationFeed option = None

    static let updateLimiter = RateController(5)

    static let logger = NLog.LogManager.GetCurrentClassLogger()

    static let httpClient =
        let hch = new HttpClientHandler()
        hch.AutomaticDecompression <- System.Net.DecompressionMethods.GZip ||| System.Net.DecompressionMethods.Deflate
        hch.AllowAutoRedirect <- false

        let hc = new HttpClient(hch)

        hc.DefaultRequestHeaders.Connection.Add("keep-alive")

        hc.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "https://github.com/CSGA-KPX/TheBot")
        |> ignore

        hc

    static let fetchUri (url: string) =
        logger.Info("访问网址:{0}", url)

        try
            let t = httpClient.GetAsync(url).ConfigureAwait(false).GetAwaiter().GetResult()

            let ret =
                t.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult()

            if t.IsSuccessStatusCode then
                Some ret
            else
                logger.Info("访问网址:{0}，失败。响应 = {1}，返回 = {2}", url, t.StatusCode, ret)
                None
        with e ->
            logger.Info("访问网址:{0}，异常：{1}", url, e.ToString())
            None

    do
        x.Update()

    member x.FeedInfo = feed

    (*
    member x.GetXml() =
        if cache.IsSome then
            let feed = lock updateSync (fun _ -> cache)
            let sb = StringBuilder()
            let xws = XmlWriterSettings(Indent = true, Encoding = Encoding.Unicode)
            use wr = XmlWriter.Create(sb, xws)
            Rss20FeedFormatter(feed.Value).WriteTo(wr)
            wr.Close()

            Some(sb.ToString())
        else
            None
    *)

    member x.FalcoHandler: HttpHandler =
        fun ctx ->
            let xmlData = 
                let feed = lock updateSync (fun _ -> cache)
                if feed.IsSome then
                    use ms = new MemoryStream()
                    let xws = XmlWriterSettings(Indent = true, Encoding = Encoding.UTF8)
                    use xw = XmlWriter.Create(ms, xws)

                    let feed = lock updateSync (fun _ -> cache)
                    feed.Value.SaveAsRss20(xw)
                    xw.Flush()
                    Some (ms.ToArray())
                else
                    None

            let handler =
                if xmlData.IsNone then
                    Response.withStatusCode 503 >> Response.ofPlainText ("FeedCacheHandler.GetXml失败")
                else
                    Response.ofBinary "application/rss+xml; charset=utf-8" [] xmlData.Value

            handler ctx

    member x.Update() =
        updateLimiter.Enqueue(fun _ -> x.DoUpdate())

    member private x.DoUpdate() =
        // 在缓存时间内 -> cache
        // 不在缓存时间内 update()

        let xml =
            match feed.PubmedRssSource with
            | File path -> Some (File.ReadAllText(path))
            | Url url -> fetchUri url

        // 如果RSS访问失败，就不更新了
        if xml.IsSome then
            try
                use sr = new StringReader(xml.Value)
                use xml = XmlReader.Create(sr)
                let feed = SyndicationFeed.Load(xml)

                RssModify.optimizeFeed feed

                lock updateSync (fun _ -> cache <- Some feed)
            with e ->
                logger.Info("访问订阅:{0}，异常：{1}", feed.FeedTitle, e.ToString())

[<EntryPoint>]
let main args =
    let externalUrl = "http://host.docker.internal:5007"
    let listenUrl = "http://0.0.0.0:5007"

    let logger = NLog.LogManager.GetLogger("MAIN")

    let updateTimer = new Timers.Timer(TimeSpan.FromDays(1.0), AutoReset = true)
    updateTimer.Start()

    let endPoints =
        [ let opml = Feeds.generateOpml externalUrl
          get
              "/opml"
              (Response.ofBinary "application/rss+xml; charset=utf-8" [] opml)
              //(Response.withContentType "application/xml; charset=utf-16"
              // >> Response.ofString Encoding.Unicode opml)

          for feed in Feeds.feeds do
              let handler = FeedCacheHandler(feed)

              updateTimer.Elapsed.Add(fun _ ->
                  logger.Info("触发自动更新{0}", handler.FeedInfo.FeedTitle)
                  handler.Update())

              get handler.FeedInfo.BindPath (handler.FalcoHandler) ]

    let wapp = WebApplication.Create()

    wapp.Urls.Clear()
    wapp.Urls.Add(listenUrl)

    for ep in endPoints do
        logger.Info("将要监听{0}", Uri(Uri(listenUrl), ep.Pattern))

    wapp.UseRouting()
        .UseFalco(endPoints)
        .Run(Response.withStatusCode 404 >> Response.ofPlainText "Not found")
    0
