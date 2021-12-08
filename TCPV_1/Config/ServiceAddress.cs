using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Config
{
    public class ServiceAddress
    {
        public string? Address { get; set; }

        public int PortNumber { get; set; }

        public override string ToString() => this.Address + ":" + (object)this.PortNumber;
    }
}
