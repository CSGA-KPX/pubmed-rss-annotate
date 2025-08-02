module KPX.Pubmed_RSS.RssUtils

open System
open System.Collections.Generic
open System.IO
open System.Text
open System.Text.RegularExpressions
open System.Xml
open System.Xml.Linq
open System.Reflection



let private regexStripXmlTag = Regex("<[^>]+>", RegexOptions.Compiled)

/// 从文本中剔除XML标签
let stripXmlTags (str) = regexStripXmlTag.Replace(str, String.Empty)

// 因为PubMed的RSS有XML错误和字段转移错误，比较难修复。所以使用人工方式修正
/// 从PubMed RSS文本中提取摘要信息，剔除DOI期刊号等无用内容
let pubmedExtract (str: string) =
    let startStub = "ABSTRACT</b></p>"
    let endStub = "</p><p style=\"color: lightgray\">"

    let startIndex = str.IndexOf(startStub) + startStub.Length
    let endStub = str.IndexOf(endStub) - 1

    let strX = str.[startIndex..endStub]
    //stripXmlTags(strX)
    strX