namespace LNDroneController.CLN
{
    public class CLNSettings
    {
        /// <summary>
        /// Grpc endpoint
        /// </summary>
        public string GrpcEndpoint { get; set; }
        /// <summary>
        /// TLS Cert as Base64 string, if provided will be perfered source
        /// </summary>
        public string TLSCertBase64 { get; set; }
        /// <summary>
        /// Macaroon Path
        /// </summary>
        public string MacaroonBase64 { get; set; }
        /// <summary>
        /// Default Fee Maximum as a percentage of total amount of invoice
        /// </summary>
    }
}