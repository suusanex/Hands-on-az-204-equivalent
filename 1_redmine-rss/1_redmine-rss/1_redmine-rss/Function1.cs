using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using redmine_rss_func;

namespace _1_redmine_rss
{
    public static class Function1
    {
        [FunctionName("RSSPollingFuncLoop")]
        public static async Task RunOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context)
        {

            var rssResult =
                await context.CallActivityAsync<(bool isChanged, IEnumerable<UpdateDocumentItem> updateEntry)>("RSSPollingFunc", null);


            await context.CreateTimer(context.CurrentUtcDateTime.AddMinutes(1), CancellationToken.None);

            context.ContinueAsNew(null);
        }


        [FunctionName("RSSPollingFuncDummy")]
        public static async Task<(bool isChanged, IEnumerable<UpdateDocumentItem> updateEntry)> RSSPollingFuncDummy([ActivityTrigger] IDurableActivityContext context, ILogger log,
            [CosmosDB("RssCheckData", "Items",
                ConnectionStringSetting = "DbRssCheckDataConnectString",
                SqlQuery = "select * from UpdateDocumentItems d ORDER BY d.Updated DESC OFFSET 0 LIMIT 1")]
            IEnumerable<UpdateDocumentItem> updateDocumentLatest,
            [CosmosDB("RssCheckData", "Items",
                ConnectionStringSetting = "DbRssCheckDataConnectString")]
            IAsyncCollector<UpdateDocumentItem> updateDocumentOut)
        {
            log.LogInformation($"RSSPollingFunc Start");
            var updateLatest = updateDocumentLatest.FirstOrDefault();
            log.LogInformation($"RSSPollingFunc Start, Latest={updateLatest}");

            if (updateLatest == null)
            {
                return (true, new[]
                {
                    new UpdateDocumentItem
                    {
                        IssueId = "1",
                        Title = "Title1",
                        Updated = DateTime.UtcNow,
                    }
                });
            }

            return (false, Array.Empty<UpdateDocumentItem>());

        }

        [FunctionName("RSSPollingFunc")]
        public static async Task<(bool isChanged, IEnumerable<UpdateDocumentItem> updateEntry)> RSSPollingFunc([ActivityTrigger] IDurableActivityContext context, ILogger log,
            [CosmosDB("RssCheckData", "Items",
                ConnectionStringSetting = "DbRssCheckDataConnectString",
                SqlQuery = "select * from UpdateDocumentItems d ORDER BY d.Updated DESC OFFSET 0 LIMIT 1")]
            IEnumerable<UpdateDocumentItem> updateDocumentLatest,
            [CosmosDB("RssCheckData", "Items",
                ConnectionStringSetting = "DbRssCheckDataConnectString")]
            IAsyncCollector<UpdateDocumentItem> updateDocumentOut)
        {
            log.LogInformation($"RSSPollingFunc Start");
            var updateLatest = updateDocumentLatest.FirstOrDefault();
            log.LogInformation($"RSSPollingFunc Start, Latest={updateLatest}");

            try
            {
                var inst = new Redmine(log);
                var checkResult = await inst.RSSCheck(updateLatest);
                if (checkResult.isChanged)
                {
                    foreach (var item in checkResult.updateEntry)
                    {
                        await updateDocumentOut.AddAsync(item);
                    }
                }

                log.LogInformation($"RSSPollingFunc Result, isChanged={checkResult.isChanged}, UpdateItems={string.Join(", ", checkResult.updateEntry)}");

                return checkResult;
            }
            catch (Exception e)
            {
                log.LogWarning($"Exception, {e}");

                return (false, Array.Empty<UpdateDocumentItem>());
            }
        }

        [FunctionName("RSSPollingFuncLoop_HttpStart")]
        public static async Task<HttpResponseMessage> HttpStart(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            // Function input comes from the request content.
            string instanceId = await starter.StartNewAsync("RSSPollingFuncLoop", null);

            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

            return starter.CreateCheckStatusResponse(req, instanceId);
        }

        [FunctionName("RSSPollingFuncLoop_HttpStop")]
        public static async Task<HttpResponseMessage> HttpStop(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            var queries = HttpUtility.ParseQueryString(req.RequestUri?.Query);
            var instanceId = queries["instanceid"];

            await starter.TerminateAsync(instanceId, "Http Stop Request");

            log.LogInformation($"Terminated orchestration with ID = '{instanceId}'.");

            return starter.CreateCheckStatusResponse(req, instanceId);
        }
    }
}