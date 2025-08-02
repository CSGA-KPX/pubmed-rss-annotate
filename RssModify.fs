module KPX.Pubmed_RSS.RssModify

open System
open System.Text
open System.Xml.Linq
open System.ServiceModel.Syndication


let private logger = NLog.LogManager.GetLogger("KPX.Pubmed_RSS.RssModify")

/// 优化并修改原有SyndicationItem
let optimizeFeedItem (item: SyndicationItem) =
    logger.Info("订阅条目{0}：开始处理", item.Id)

    let content =
        let t = item.ElementExtensions |> Seq.find (fun ext -> ext.OuterName = "encoded")
        item.ElementExtensions.Remove(t) |> ignore
        //let text = t.GetObject<XElement>().Value
        let text = RssUtils.pubmedExtract (t.GetObject<XElement>().Value)

        //logger.Trace("订阅条目{0}：encoded = /{1}/", item.Id, t.GetObject<XElement>().Value)
        //logger.Trace("订阅条目{0}：text = /{1}/", item.Id, text)

        if String.IsNullOrWhiteSpace(text.Trim()) then
            None
        else
            Some(text)

    let journal =
        let t = item.ElementExtensions |> Seq.find (fun ext -> ext.OuterName = "source")
        t.GetObject<XElement>().Value

    let rssComments =
        let sb = StringBuilder()

        sb
            .AppendLine("<div>")
            .AppendLine($"<p><b>文献标题：</b>{item.Title.Text}</p>")
            .AppendLine($"<p><b>期刊名称：</b>{journal}</p>")
        |> ignore

        let ifInfo = IFDatabase.ifLookup (journal)

        if ifInfo.IsSome then
            sb.AppendLine($"<p><b>期刊信息：</b>{ifInfo.Value}</p>") |> ignore
        else
            logger.Warn("订阅条目{0}：期刊{1}没有影响因子", item.Id, journal)

        if content.IsNone then
            sb.AppendLine("<p><b>无摘要</b></p>") |> ignore
        else
            sb.AppendLine($"<p>{content.Value}</p>") |> ignore

        sb.AppendLine("<div>").ToString()

    item.Summary <- TextSyndicationContent.CreateXhtmlContent(rssComments)

/// 优化并修改原有SyndicationFeed
let optimizeFeed (feed: SyndicationFeed) =

    logger.Info("订阅 {0} ：开始处理", feed.Title.Text)

    logger.Info("订阅 {0} ：移除self链接", feed.Title.Text)
    // 移除掉歧义的self链接
    let selfLink = feed.Links |> Seq.find (fun l -> l.RelationshipType = "self")
    feed.Links.Remove(selfLink) |> ignore

    // 万一处理炸了也能原样输出
    try
        logger.Info("订阅 {0} ：更新SyndicationItems", feed.Title.Text)
        let newItems =
            feed.Items
            |> Seq.map (fun item ->
                let copy = item.Clone()
                optimizeFeedItem copy
                copy)
            |> Seq.toArray

        feed.Items <- newItems
    with e ->
        logger.Error("订阅 {0} ：更新SyndicationItems失败：{1}", feed.Title.Text, e.ToString())
