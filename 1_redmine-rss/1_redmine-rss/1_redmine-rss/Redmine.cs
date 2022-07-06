using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;

namespace redmine_rss_func
{
    public class Redmine
    {
        public Redmine(ILogger log)
        {
            m_Log = log;
        }

        private readonly ILogger m_Log;


        public async Task<IEnumerable<UpdateDocumentItem>> RSSGet()
        {
            //手順1. チェック対象の更新が含まれるようにしたRSSのURLと、RSSアクセス用のキーを用意
            var rssUrl = $"http://your-redmine-root-url/projects/your-project-name/activity.atom";
            
            var atomKey = "f6012197692df72c5bb59698430e8143aeff13bb";

            //手順2. 普通のHttpClientを用意し、キー付きでRSSをGet
            using var client = new HttpClient();
            {
                var res = await client.GetAsync($"{rssUrl}?key={atomKey}");
                if (!res.IsSuccessStatusCode)
                {
                    throw new Exception($"RSS Get Fail, {nameof(res.StatusCode)}={res.StatusCode}, {await res.Content.ReadAsStringAsync()} {rssUrl}");
                }

                var rssStr = await res.Content.ReadAsStringAsync();

                //手順3. RSSのXMLを読み込み、UpdateDocumentItemという型（独自）に変換
                var xdoc = XDocument.Parse(rssStr);
                var xns = xdoc.Root.Name.Namespace;
                var entries = xdoc.Descendants(xns + "entry");

                return entries.Select(d => new UpdateDocumentItem
                {
                    Updated = DateTime.TryParse(d.Element(xns + "updated")?.Value, out var updated) ? updated.ToUniversalTime() : null,
                    IssueId = d.Element(xns + "id")?.Value,
                    Title = d.Element(xns + "title")?.Value,
                });


            }


        }

        public async Task<(bool isIncludeAttachments, IEnumerable<string> attachmentsUrls)> GetAttachmentsInfo(
            int issueId, int? journalId)
        {
            var redmineRootUrl = "http://your-redmine-url/";
            var apiKey = "6f81e2188cb2183f3244791d85c5cccee1818221";

            if (journalId == null)
            {
                //issue自体の添付ファイルを取る
                using var client = new HttpClient();
                {
                    var attachmentIds = new List<int>();

                    {
                        var reqUrl = $"{redmineRootUrl}issues/{issueId}.json?include=journals";
                        var req = new HttpRequestMessage(HttpMethod.Get, reqUrl);
                        req.Headers.Add("X-Redmine-API-Key", apiKey);

                        var res = await client.SendAsync(req);
                        if (!res.IsSuccessStatusCode)
                        {
                            throw new Exception(
                                $"issue ID={issueId} Get Fail, {nameof(res.StatusCode)}={res.StatusCode}, {await res.Content.ReadAsStringAsync()} {reqUrl}");
                        }

                        var resStr = await res.Content.ReadAsStringAsync();
                        m_Log.LogInformation($"get json, {resStr}");
                        var node = JsonNode.Parse(resStr);

                        var journals = node?["issue"]?["journals"];
                        if (journals == null)
                        {
                            m_Log.LogInformation($"issue ID={issueId} journals is null");
                            return (false, Array.Empty<string>());

                        }

                        foreach (var journal in journals.AsArray())
                        {
                            var details = journal["details"];
                            if(details == null) continue;
                            foreach (var detail in details.AsArray())
                            {
                                if(detail?["property"]?.GetValue<string>() != "attachment") continue;

                                if (!int.TryParse(detail!["name"]?.GetValue<string>(), out int id))
                                {
                                    continue;
                                }

                                m_Log.LogInformation($"Attachment Found IssueId={id}");
                                attachmentIds.Add((int)id);

                            }

                        }
                    }

                    var attachmentUrls = new List<string>();
                    foreach (var attachmentId in attachmentIds)
                    {
                        
                        var reqUrl = $"{redmineRootUrl}attachments/{attachmentId}.json";
                        var req = new HttpRequestMessage(HttpMethod.Get, reqUrl);
                        req.Headers.Add("X-Redmine-API-Key", apiKey);
                        var res = await client.SendAsync(req);
                        if (!res.IsSuccessStatusCode)
                        {
                            throw new Exception(
                                $"attachment ID={attachmentId} Get Fail, {nameof(res.StatusCode)}={res.StatusCode}, {await res.Content.ReadAsStringAsync()} {reqUrl}");
                        }

                        var resStr = await res.Content.ReadAsStringAsync();
                        m_Log.LogInformation($"get json, {resStr}");

                        var node = JsonNode.Parse(resStr);
                        
                        var url = node?["attachment"]?["content_url"];
                        if (url == null)
                        {
                            m_Log.LogWarning($"Attachment URL Not Found, id={attachmentId}");
                            continue;
                        }

                        var urlStr = url.GetValue<string>();
                        m_Log.LogInformation($"attachmentUrl Add, {urlStr}");
                        attachmentUrls.Add(urlStr);

                    }

                    return (attachmentUrls.Any(), attachmentUrls);

                }



            }
            else
            {
                //issueの特定journalの添付ファイルを取る

                //var content = new StringContent(reqJson.ToString(), Encoding.UTF8, "application/json");
                //content.Headers.Add("X-Redmine-API-Key", apiKey);
                throw new NotImplementedException();

            }
        }

    }
}
