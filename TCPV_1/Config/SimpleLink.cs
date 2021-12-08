using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Config
{
    public class SimpleLink
    {
        public EndPoint EndPoint { get; set; }

        public List<SourcePoint> Sources { get; set; }

        public ServiceAddress ServiceAddress { get; set; }

        public SimpleLink()
        {
            this.EndPoint = new EndPoint();
            this.Sources = new List<SourcePoint>();
            this.ServiceAddress = new ServiceAddress();
        }
    }
}