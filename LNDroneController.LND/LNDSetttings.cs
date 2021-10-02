using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LNDroneController.LND
{
    public class LNDSettings
    {
        /// <summary>
        /// LND Host Grpc Endpoint (e.g. https://localhost:10009)
        /// </summary>
        public string GrpcEndpoint { get; set; }
        /// <summary>
        /// TLS Certificate Path
        /// </summary>
        public string TLSCertPath { get; set; }
        /// <summary>
        /// TLS Cert as Base64 string, if provided will be perfered source
        /// </summary>
        public string TLSCertBase64 { get; set; }
        /// <summary>
        /// Macaroon Path
        /// </summary>
        public string MacaroonPath { get; set; }
        /// <summary>
        /// Macaroon as Base64 string, if provided will be perfered source
        /// </summary>
        public string MacaroonBase64 { get; set; }
        /// <summary>
        /// Default Fee Maximum as a percentage of total amount of invoice
        /// </summary>
        public double MaxFeePercentage { get; set; } = 0.01;
        /// <summary>
        /// Default Max Fee willing to pay in sats
        /// </summary>
        public long MaxFeeSats { get; set; } = 250;
        /// <summary>
        /// Default Minimum Fee: 
        /// </summary>
        public long MinFeeSats { get; set; } = 10;
    }
}
