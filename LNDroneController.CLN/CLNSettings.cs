using System.Security.Cryptography.X509Certificates;

namespace LNDroneController.CLN
{
    public class CLNSettings
    {
        /// <summary>
        /// Grpc endpoint
        /// </summary>
        public string GrpcEndpoint { get; set; }
        /// <summary>
        /// Provide client side TLS cert w/ key
        /// </summary>
        public X509Certificate2 ClientCertWithKey { get; set; }
    }
}