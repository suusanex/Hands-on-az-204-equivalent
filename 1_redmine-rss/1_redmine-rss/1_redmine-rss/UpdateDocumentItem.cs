using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace redmine_rss_func
{
    public class UpdateDocumentItem
    {
        public DateTime? Updated { get; set; }
        public string IssueId { get; set; }
        public string Title { get; set; }

        public override string ToString()
        {
            return $"{nameof(UpdateDocumentItem)}:" +
                   string.Join(",", Updated, IssueId, Title);
        }
    }
}
