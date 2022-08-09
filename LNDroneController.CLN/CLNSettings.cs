using System.Security.Cryptography.X509Certificates;

namespace LNDroneController.CLN
{
    public class CLNSettings
    {
        /// <summary>
        /// Grpc endpoint
        /// </summary>
        public string GrpcEndpoint { get; set; }

        public string ClientCertBase64 { get; set; }
        public string ClientKeyBase64 { get; set; }

        /// <summary>
        /// Provide client side TLS cert w/  or populate with ClientCertBase64 and ClientKeyBase64 fields
        /// </summary>
        public X509Certificate2 ClientCertWithKey { get; set; }

        public bool CertBugFixFlag { get; set; }
    }
}