namespace KPX.Pubmed_RSS.RateController

open System
open System.Collections.Generic


type private TaskSchedulerMessage =
    | Enqueue of Action * AsyncReplyChannel<unit>
    | Finished

type RateController(maxParallel: int) =
    let logger = NLog.LogManager.GetCurrentClassLogger()
    let updateQueue = Queue<_>()
    let mutable currentConcurrent = 0

    let agent =
        MailboxProcessor.Start(fun inbox ->
            async {
                while true do
                    let! msg = inbox.Receive()

                    match msg with
                    | Enqueue(action, reply) -> updateQueue.Enqueue(action, reply)
                    | Finished -> currentConcurrent <- currentConcurrent - 1

                    if currentConcurrent < maxParallel && updateQueue.Count > 0 then
                        currentConcurrent <- currentConcurrent + 1
                        let (action, reply) = updateQueue.Dequeue()

                        async {
                            action.Invoke()
                            reply.Reply()
                            inbox.Post(Finished)
                        }
                        |> Async.Start

                    if currentConcurrent >= maxParallel && updateQueue.Count > 0 then
                        logger.Warn("队列已满，当前并发：{0}，队列数：{1}。", currentConcurrent, updateQueue.Count)
            })

    member x.Enqueue(action: Action) =
        agent.PostAndReply(fun reply -> Enqueue(action, reply))